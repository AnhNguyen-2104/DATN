using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using WpfYolo_DotNet.Devices;

namespace PlateVision50.Vision;

/// <summary>
/// Owns one station camera: SDK-side discovery, exclusive open, frame counting and disposal.
/// One instance belongs to exactly one station; it never shares a service or frame loop.
/// </summary>
public sealed class StationCameraController : IDisposable
{
    private readonly int _slot;
    private readonly object _gate = new();
    private UnifiedCameraService? _service;
    private HikDirectIpCamera? _direct;
    private long _serviceFrames;
    private bool _opened;

    public StationCameraController(int slot, string name)
    {
        _slot = slot;
        Model = new CameraSlotModel(slot, name);
    }

    public CameraSlotModel Model { get; }
    public int SlotNumber => _slot;
    public bool IsOpen { get { lock (_gate) return _opened; } }
    public long FrameNumber => _direct is { } direct ? direct.FrameNumber : Interlocked.Read(ref _serviceFrames);
    public string LastSdkError { get; private set; } = "";

    /// <summary>Enumerates every supported SDK device for the selected mode. Safe to call off the UI thread.</summary>
    public IReadOnlyList<CameraDevice> Scan(CameraTransportMode mode)
    {
        var found = new List<CameraDevice>();

        if (mode != CameraTransportMode.Ip)
            AddTransport(found, CameraTransportType.Usb, "USB");
        if (mode != CameraTransportMode.Usb)
            AddTransport(found, CameraTransportType.GigE, "GigE");

        return found;
    }

    private static void AddTransport(List<CameraDevice> found, CameraTransportType transport, string label)
    {
        UnifiedCameraService? service = null;
        try
        {
            service = new UnifiedCameraService();
            service.Enumerate(transport);
            foreach (CameraInfo info in service.CameraList)
            {
                if (info.Transport != transport) continue;
                found.Add(new CameraDevice(
                    info.Vendor.ToString(),
                    label,
                    ShortName(info.Name),
                    string.IsNullOrWhiteSpace(info.SerialNumber)
                        ? label + "#" + info.BackendIndex.ToString(CultureInfo.InvariantCulture)
                        : info.SerialNumber,
                    "",
                    info.BackendIndex));
            }
        }
        catch
        {
            // A missing or unlicensed SDK is reported by the caller as an empty scan; it is not a fake device.
        }
        finally
        {
            service?.Dispose();
        }
    }

    private static string ShortName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Camera";
        int marker = name.IndexOf("]:", StringComparison.Ordinal);
        string value = marker >= 0 ? name[(marker + 2)..] : name;
        int dash = value.IndexOf(" - ", StringComparison.Ordinal);
        return (dash > 0 ? value[..dash] : value).Trim();
    }

    /// <summary>Claims the device, opens only its vendor SDK path and starts acquisition. No PLC or motion call.</summary>
    public bool Connect(CameraDevice device, CameraTransportMode mode, string ipText, string nicText,
        CameraOwnershipRegistry registry, out string error)
    {
        error = "";
        if (device is null) { error = "Select a camera device"; return false; }

        if (!CameraTransportPolicy.CanDispatch(device, mode, out error)) return false;

        if (mode == CameraTransportMode.Ip && !CameraTransportPolicy.IsValidAddress(ipText))
        {
            error = "Enter a valid IPv4 address or hostname";
            return false;
        }

        if (!registry.TryClaim(device.Key, _slot, out error)) return false;

        lock (_gate)
        {
            DisposeSource();
            Model.MarkConnecting();
        }

        try
        {
            if (device.BackendIndex < 0)
            {
                if (!string.Equals(device.Vendor, "Hikvision", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Direct IP connect is only available for Hikrobot GigE cameras");

                if (!IPAddress.TryParse(ipText, out IPAddress? cameraAddress) ||
                    cameraAddress.AddressFamily != AddressFamily.InterNetwork)
                    throw new ArgumentException("Hikrobot direct IP requires an IPv4 address");

                string hostIp = string.IsNullOrWhiteSpace(nicText) ? FindLocalIp(cameraAddress) : nicText.Trim();
                if (hostIp.Length == 0)
                    throw new ArgumentException("Enter the local NIC IPv4 address for this camera subnet");

                var direct = new HikDirectIpCamera(cameraAddress.ToString(), hostIp);
                try { direct.Open(); }
                catch { direct.Dispose(); throw; }
                if (!direct.IsOpen) throw new InvalidOperationException("Camera did not start acquisition");

                lock (_gate) { _direct = direct; _opened = true; }
            }
            else
            {
                var service = new UnifiedCameraService();
                try
                {
                    CameraTransportType transport = device.Transport.Contains("USB", StringComparison.OrdinalIgnoreCase)
                        ? CameraTransportType.Usb
                        : CameraTransportType.GigE;
                    service.Enumerate(transport);

                    CameraInfo? info = service.CameraList.FirstOrDefault(c =>
                        c.BackendIndex == device.BackendIndex &&
                        c.Transport == transport &&
                        c.Vendor.ToString().Equals(device.Vendor, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(device.Serial) || c.SerialNumber == device.Serial));

                    if (info is null) throw new InvalidOperationException("Camera disappeared from the " + device.Transport + " scan");
                    if (!service.Open(info)) throw new InvalidOperationException("Open refused by " + device.Vendor + " SDK");
                    service.FrameReceived += _ => Interlocked.Increment(ref _serviceFrames);
                    service.Start();

                    lock (_gate) { _service = service; _opened = true; }
                }
                catch
                {
                    service.Dispose();
                    throw;
                }
            }

            Model.MarkConnected();
            return true;
        }
        catch (Exception ex)
        {
            LastSdkError = ex.Message;
            RegistryRelease(registry);
            lock (_gate) { DisposeSource(); Model.MarkError(Terse(ex)); }
            error = Terse(ex);
            return false;
        }
    }

    private void RegistryRelease(CameraOwnershipRegistry registry)
    {
        // The slot owns nothing else; releasing here also covers a failed half-open.
        registry.Release(_slot);
    }

    private static string Terse(Exception ex)
    {
        string message = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length <= 160 ? message : message[..160];
    }

    /// <summary>Clones the latest SDK frame; null when no frame has been received.</summary>
    public Mat? CloneLatestFrame()
    {
        lock (_gate)
        {
            if (_direct is not null) return _direct.CloneLatestFrame();
            return _service?.CloneLatestFrame();
        }
    }

    /// <summary>Updates Connected/LIVE/NO FRAME from real frame timestamps.</summary>
    public void RefreshState(DateTime utcNow, TimeSpan staleAfter)
    {
        if (Model.State == CameraSlotState.Live && !Model.IsLive(utcNow, staleAfter))
        {
            Model.MarkLost("No frame for " + staleAfter.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
            return;
        }

        if (Model.State == CameraSlotState.Connected && IsOpen)
            Model.MarkConnected();
    }

    public void Disconnect(CameraOwnershipRegistry registry)
    {
        lock (_gate)
        {
            DisposeSource();
            Model.MarkOffline();
        }
        registry.Release(_slot);
    }

    private void DisposeSource()
    {
        _opened = false;
        var direct = _direct;
        _direct = null;
        if (direct is not null) { try { direct.Dispose(); } catch { } }

        var service = _service;
        _service = null;
        if (service is not null) { try { service.Dispose(); } catch { } }
        Interlocked.Exchange(ref _serviceFrames, 0);
    }

    /// <summary>Finds a local IPv4 address on the camera's /24 subnet (GigE point-to-point adapter).</summary>
    public static string FindLocalIp(IPAddress cameraAddress)
    {
        byte[] camera = cameraAddress.GetAddressBytes();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] local = address.Address.GetAddressBytes();
                    if (local[0] == camera[0] && local[1] == camera[1] && local[2] == camera[2] && !local.SequenceEqual(camera))
                        return address.Address.ToString();
                }
            }
        }
        catch { }
        return "";
    }

    public void Dispose()
    {
        lock (_gate) { DisposeSource(); }
    }
}

/// <summary>Per-station transport/device/IP preference, stored without credentials.</summary>
public static class CameraSlotSettingsStore
{
    private sealed record SlotSetting(string Mode, string Ip, string Nic, string DeviceKey);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlateVision50_V2", "camera_slots.json");

    public static (CameraTransportMode Mode, string Ip, string Nic, string DeviceKey) Load(int slot)
    {
        try
        {
            if (!File.Exists(FilePath)) return (CameraTransportMode.Auto, "", "", "");
            var all = JsonSerializer.Deserialize<Dictionary<string, SlotSetting>>(File.ReadAllText(FilePath));
            if (all is null || !all.TryGetValue(slot.ToString(CultureInfo.InvariantCulture), out var value) || value is null)
                return (CameraTransportMode.Auto, "", "", "");
            CameraTransportMode mode = Enum.TryParse(value.Mode, true, out CameraTransportMode parsed)
                ? parsed : CameraTransportMode.Auto;
            return (mode, value.Ip ?? "", value.Nic ?? "", value.DeviceKey ?? "");
        }
        catch
        {
            return (CameraTransportMode.Auto, "", "", "");
        }
    }

    public static void Save(int slot, CameraTransportMode mode, string ip, string nic, string deviceKey)
    {
        try
        {
            Dictionary<string, SlotSetting> all = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, SlotSetting>>(File.ReadAllText(FilePath)) ?? new()
                : new();
            all[slot.ToString(CultureInfo.InvariantCulture)] =
                new SlotSetting(mode.ToString(), ip ?? "", nic ?? "", deviceKey ?? "");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch
        {
            // Preference persistence is best effort; it must never break camera operation.
        }
    }
}
