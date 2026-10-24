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
using BaslerSdk = NVKProject.DotNet.BaslerCamera;

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

    /// <summary>
    /// Enumerates only the selected transport. Vendor SDK failures become scan issues and never hide
    /// devices from the other SDK; IP mode adds read-only GVCP discovery on the camera NIC.
    /// Safe to call off the UI thread and never throws.
    /// </summary>
    public CameraScanResult Scan(CameraTransportMode mode, string ipText = "", string nicText = "")
    {
        var devices = new List<CameraDevice>();
        var issues = new List<CameraScanIssue>();

        try
        {
            if (mode == CameraTransportMode.Usb)
            {
                EnumerateHik(devices, issues, CameraTransportType.Usb, "USB");
                EnumerateBasler(devices, issues, BaslerSdk.CameraTransportType.Usb, "USB");
            }
            else
            {
                EnumerateHik(devices, issues, CameraTransportType.GigE, "GigE");
                EnumerateBasler(devices, issues, BaslerSdk.CameraTransportType.GigE, "GigE");
                DiscoverGvcp(devices, issues, ipText, nicText);
            }
        }
        catch (Exception ex)
        {
            issues.Add(CreateIssue("Camera", mode == CameraTransportMode.Usb ? "USB" : "GigE", ex));
        }

        foreach (CameraScanIssue issue in issues) LogScanIssue(mode, issue);

        return new CameraScanResult(DedupeSdkFirst(devices), issues);
    }

    private static void EnumerateHik(List<CameraDevice> devices, List<CameraScanIssue> issues,
        CameraTransportType transport, string label)
    {
        HikCameraService? service = null;
        try
        {
            service = new HikCameraService();
            service.Enumerate(transport);
            if (!string.IsNullOrWhiteSpace(service.LastError))
            {
                issues.Add(new CameraScanIssue("Hikvision", label, "MvCameraSdkError", service.LastError,
                    "MvCameraControl.dll enumeration failed for " + label + " transport."));
            }

            foreach (CameraInfo info in service.CameraList)
            {
                if (info.Transport != transport) continue;
                service.DeviceAddresses.TryGetValue(info.BackendIndex, out string address);
                devices.Add(new CameraDevice(
                    info.Vendor.ToString(),
                    label,
                    ShortName(info.Name),
                    string.IsNullOrWhiteSpace(info.SerialNumber)
                        ? label + "#" + info.BackendIndex.ToString(CultureInfo.InvariantCulture)
                        : info.SerialNumber,
                    address ?? string.Empty,
                    info.BackendIndex));
            }
        }
        catch (Exception ex)
        {
            issues.Add(CreateIssue("Hikvision", label, ex));
        }
        finally
        {
            service?.Dispose();
        }
    }

    private static void EnumerateBasler(List<CameraDevice> devices, List<CameraScanIssue> issues,
        BaslerSdk.CameraTransportType transport, string label)
    {
        BaslerSdk.BaslerCameraService? service = null;
        try
        {
            service = new BaslerSdk.BaslerCameraService();
            service.Enumerate(transport);
            if (!string.IsNullOrWhiteSpace(service.LastError))
            {
                issues.Add(new CameraScanIssue("Basler", label, "PylonSdkError", service.LastError,
                    service.LastErrorDetails));
            }

            foreach (BaslerSdk.BaslerCameraDeviceInfo info in service.CameraList)
            {
                if (info.Transport != transport) continue;
                string model = string.IsNullOrWhiteSpace(info.ModelName) ? ShortName(info.Name) : info.ModelName.Trim();
                devices.Add(new CameraDevice(
                    "Basler",
                    label,
                    model,
                    string.IsNullOrWhiteSpace(info.SerialNumber)
                        ? "BASLER#" + info.DeviceListIndex.ToString(CultureInfo.InvariantCulture)
                        : info.SerialNumber,
                    string.Empty,
                    info.DeviceListIndex));
            }
        }
        catch (Exception ex)
        {
            issues.Add(CreateIssue("Basler", label, ex));
        }
        finally
        {
            service?.Dispose();
        }
    }

    private void DiscoverGvcp(List<CameraDevice> devices, List<CameraScanIssue> issues, string ipText, string nicText)
    {
        string nic = ResolveCameraNic(ipText, nicText);
        if (nic.Length == 0) return;

        try
        {
            GvcpScanResult result = GvcpDiscovery.DiscoverAsync(nic, ipText, 1000).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                issues.Add(new CameraScanIssue("GVCP", "GigE", "GvcpDiscoveryError", result.Error,
                    "Read-only GVCP discovery on local NIC " + nic + " for target " +
                    (string.IsNullOrWhiteSpace(ipText) ? "broadcast" : ipText.Trim()) + "."));
            }

            foreach (GvcpDeviceInfo info in result.Devices)
            {
                // Direct Hik open exists only for Hikrobot, so other identified vendors stay SDK-only.
                if (!info.IsHikrobot) continue;
                CameraDevice? device = info.ToCameraDevice();
                if (device is not null) devices.Add(device);
            }
        }
        catch (Exception ex)
        {
            issues.Add(CreateIssue("GVCP", "GigE", ex));
        }
    }

    private static CameraScanIssue CreateIssue(string vendor, string transport, Exception ex)
    {
        string details = "HRESULT=0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture) +
                         Environment.NewLine + ex;
        return new CameraScanIssue(vendor, transport, ex.GetType().Name, Terse(ex), details);
    }

    private static string TerseText(string? text, int limit)
    {
        string value = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length <= limit ? value : value[..limit];
    }

    private static readonly string ScanLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlateVision50_V2", "logs", "camera_scan.log");

    private static void LogScanIssue(CameraTransportMode mode, CameraScanIssue issue)
    {
        try
        {
            string? directory = Path.GetDirectoryName(ScanLogPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                + " | " + mode.ToString().ToUpperInvariant()
                + " | " + (issue.Vendor ?? string.Empty)
                + " | " + (issue.Transport ?? string.Empty)
                + " | " + (issue.ErrorType ?? string.Empty)
                + " | " + TerseText(issue.Message, 400)
                + " | " + TerseText(issue.Details, 800);

            File.AppendAllText(ScanLogPath, line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never break a scan.
        }
    }

    private static IReadOnlyList<CameraDevice> DedupeSdkFirst(List<CameraDevice> devices)
    {
        var result = new List<CameraDevice>();
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // SDK devices first: a GVCP-only entry with the same serial is dropped.
        foreach (CameraDevice device in devices)
            if (device.BackendIndex >= 0) AddUniqueDevice(device, serials, result);

        foreach (CameraDevice device in devices)
            if (device.BackendIndex < 0) AddUniqueDevice(device, serials, result);

        return result;
    }

    private static void AddUniqueDevice(CameraDevice device, HashSet<string> serials, List<CameraDevice> result)
    {
        string serial = (device.Serial ?? string.Empty).Trim();
        if (serial.Length > 0 && !serials.Add(serial)) return;
        result.Add(device);
    }

    /// <summary>Preferred local IPv4 for GVCP: typed NIC, camera subnet, then Ethernet 2 (169.254.113.x).</summary>
    public static string ResolveCameraNic(string? cameraIp, string? nicText)
    {
        if (!string.IsNullOrWhiteSpace(nicText) &&
            IPAddress.TryParse(nicText.Trim(), out IPAddress? nic) &&
            nic.AddressFamily == AddressFamily.InterNetwork)
        {
            return nic.ToString();
        }

        if (!string.IsNullOrWhiteSpace(cameraIp) &&
            IPAddress.TryParse(cameraIp.Trim(), out IPAddress? camera) &&
            camera.AddressFamily == AddressFamily.InterNetwork)
        {
            string local = FindLocalIp(camera);
            if (local.Length > 0) return local;
        }

        return FindPreferredCameraIp();
    }

    private static string FindPreferredCameraIp()
    {
        string fallback = string.Empty;

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (IsPlcOrVirtualInterface(nic)) continue;

                foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] bytes = address.Address.GetAddressBytes();

                    if (bytes[0] == 169 && bytes[1] == 254)
                    {
                        string value = address.Address.ToString();
                        if (value == "169.254.113.10") return value;
                        if (bytes[2] == 113 && fallback.Length == 0) fallback = value;
                        if (fallback.Length == 0) fallback = value;
                    }
                }
            }
        }
        catch { }

        return fallback;
    }

    private static bool IsPlcOrVirtualInterface(NetworkInterface nic)
    {
        string name = ((nic.Name ?? string.Empty) + " " + (nic.Description ?? string.Empty)).ToUpperInvariant();
        return name.Contains("TWINCAT") || name.Contains("ETHERCAT") || name.Contains("PLC") ||
               name.Contains("TAILSCALE") || name.Contains("LOOPBACK") || name.Contains("WSL") ||
               name.Contains("HYPER-V") || name.Contains("VMWARE") || name.Contains("VIRTUALBOX");
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

        string typedAddress = (ipText ?? string.Empty).Trim();
        string dispatchAddress = device.BackendIndex == -2 && !string.IsNullOrWhiteSpace(device.Address)
            ? device.Address.Trim()
            : typedAddress;

        if (mode == CameraTransportMode.Ip && !CameraTransportPolicy.IsValidAddress(dispatchAddress))
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
                if (!CameraTransportPolicy.IsHikVendor(device.Vendor))
                    throw new InvalidOperationException("Direct IP connect is only available for Hikrobot/Hikvision GigE cameras");

                if (!IPAddress.TryParse(dispatchAddress, out IPAddress? cameraAddress) ||
                    cameraAddress.AddressFamily != AddressFamily.InterNetwork)
                    throw new ArgumentException("Hikrobot direct IP requires an IPv4 address");

                string hostIp = ResolveCameraNic(cameraAddress.ToString(), nicText);
                if (hostIp.Length == 0)
                    throw new ArgumentException("Enter the local NIC IPv4 address for this camera subnet");

                // Typed manual IP must be confirmed as a Hikrobot camera by a read-only GVCP ACK
                // before any MVS open. Scanned GVCP devices already carry that identity.
                if (device.BackendIndex == -1)
                {
                    GvcpScanResult probe;
                    try
                    {
                        probe = Task.Run(() => GvcpDiscovery.DiscoverAsync(hostIp, cameraAddress.ToString(), 900))
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("GVCP identity check failed: " + Terse(ex));
                    }

                    GvcpDeviceInfo? identity = probe.Devices.FirstOrDefault(d =>
                        string.Equals(d.Address, cameraAddress.ToString(), StringComparison.OrdinalIgnoreCase) &&
                        d.IsIdentified && d.IsHikrobot);

                    if (identity is null)
                    {
                        string detail = string.IsNullOrWhiteSpace(probe.Error) ? "" : " (" + probe.Error + ")";
                        throw new InvalidOperationException(
                            "GVCP identity not confirmed for " + cameraAddress + ": no Hikrobot manufacturer/model ACK" + detail);
                    }
                }

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
                    if (!service.Open(info))
                        throw new InvalidOperationException(service.LastError.Length > 0
                            ? service.LastError
                            : "Open refused by " + device.Vendor + " SDK");
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
        string fallback = string.Empty;
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (IsPlcOrVirtualInterface(nic)) continue;

                foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] local = address.Address.GetAddressBytes();
                    if (local[0] != camera[0] || local[1] != camera[1] || local[2] != camera[2]) continue;
                    if (local.SequenceEqual(camera)) continue;

                    string value = address.Address.ToString();
                    if (value == "169.254.113.10") return value;
                    if (fallback.Length == 0) fallback = value;
                }
            }
        }
        catch { }
        return fallback;
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
        CameraTransportMode defaultMode = CameraSlotDefaults.DefaultMode(slot);

        try
        {
            if (!File.Exists(FilePath)) return (defaultMode, "", "", "");
            var all = JsonSerializer.Deserialize<Dictionary<string, SlotSetting>>(File.ReadAllText(FilePath));
            if (all is null || !all.TryGetValue(slot.ToString(CultureInfo.InvariantCulture), out var value) || value is null)
                return (defaultMode, "", "", "");

            return (CameraSlotDefaults.MigrateMode(value.Mode, slot), value.Ip ?? "", value.Nic ?? "", value.DeviceKey ?? "");
        }
        catch
        {
            return (defaultMode, "", "", "");
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
