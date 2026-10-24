using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlateVision50.Vision;

/// <summary>Decoded identity from a GVCP DISCOVERY_ACK packet.</summary>
public sealed record GvcpDeviceInfo(
    string Address,
    string MacAddress,
    string Manufacturer,
    string Model,
    string Version,
    string SerialNumber)
{
    /// <summary>A device is only confirmed when manufacturer and model are present; never from a bare ping.</summary>
    public bool IsIdentified =>
        !string.IsNullOrWhiteSpace(Manufacturer) &&
        !string.IsNullOrWhiteSpace(Model);

    /// <summary>True when manufacturer or model indicates Hikrobot/Hikvision GigE hardware.</summary>
    public bool IsHikrobot =>
        (Manufacturer?.Contains("Hikrobot", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (Manufacturer?.Contains("Hikvision", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (Model?.StartsWith("MV-", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Converts an identified GVCP ACK to a camera device for the station slot.</summary>
    public CameraDevice? ToCameraDevice()
    {
        if (!IsIdentified) return null;

        return new CameraDevice(
            "Hikrobot",
            "GigE",
            Model.Trim(),
            SerialNumber?.Trim() ?? string.Empty,
            Address?.Trim() ?? string.Empty,
            -2);
    }
}

/// <summary>Outcome of GVCP network enumeration.</summary>
public sealed record GvcpScanResult(IReadOnlyList<GvcpDeviceInfo> Devices, string Error);

/// <summary>Read-only GigE Vision Discovery, using UDP GVCP 3956 on a chosen local NIC.</summary>
public static class GvcpDiscovery
{
    /// <summary>
    /// Parses a GVCP DISCOVERY_ACK packet.
    /// Real offsets verified on MV-CS016-10GC:
    /// Payload starts at 8; MAC 18..23, currentIP 44..47, manufacturer 80..111,
    /// model 112..143, version 144..175, serial 224..239.
    /// Returns null if packet is null or too short to contain identity.
    /// </summary>
    public static GvcpDeviceInfo? ParseDiscoveryAck(byte[]? packet, IPAddress endpoint)
    {
        if (packet is null || packet.Length <= 80) return null;
        if (packet[2] != 0x00 || packet[3] != 0x03) return null;

        // Real datagrams carry the 8-byte GVCP header before the discovery payload.
        // A payload-only buffer is accepted as a fallback so both layouts decode.
        GvcpDeviceInfo withHeader = ParseIdentity(packet, 8, endpoint);
        if (withHeader.IsIdentified) return withHeader;

        GvcpDeviceInfo withoutHeader = ParseIdentity(packet, 0, endpoint);
        return withoutHeader.IsIdentified ? withoutHeader : withHeader;
    }

    private static GvcpDeviceInfo ParseIdentity(byte[] packet, int payloadStart, IPAddress? endpoint)
    {
        // MAC at payload 10..15
        string mac = string.Empty;
        int macOffset = payloadStart + 10;
        if (packet.Length >= macOffset + 6)
        {
            mac = string.Format(
                "{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                packet[macOffset], packet[macOffset + 1], packet[macOffset + 2],
                packet[macOffset + 3], packet[macOffset + 4], packet[macOffset + 5]);
        }

        // Current IP at payload 36..39; fall back to the UDP endpoint when unset.
        string address = endpoint?.ToString() ?? string.Empty;
        int ipOffset = payloadStart + 36;
        if (packet.Length >= ipOffset + 4)
        {
            byte b0 = packet[ipOffset], b1 = packet[ipOffset + 1];
            byte b2 = packet[ipOffset + 2], b3 = packet[ipOffset + 3];
            if (b0 != 0 || b1 != 0 || b2 != 0 || b3 != 0)
                address = new IPAddress(new[] { b0, b1, b2, b3 }).ToString();
        }

        string manufacturer = ReadAscii(packet, payloadStart + 72, 32);
        string model = ReadAscii(packet, payloadStart + 104, 32);
        string version = ReadAscii(packet, payloadStart + 136, 32);
        string serial = ReadAscii(packet, payloadStart + 216, 16);

        return new GvcpDeviceInfo(address, mac, manufacturer, model, version, serial);
    }

    /// <summary>
    /// Sends a read-only GVCP DISCOVERY_CMD to UDP port 3956 on localNic (broadcast and optional unicast),
    /// parses DISCOVERY_ACK responses, and returns discovered devices without changing camera settings.
    /// </summary>
    public static async Task<GvcpScanResult> DiscoverAsync(string localIp, string? targetIp, int timeoutMs = 1000)
    {
        if (string.IsNullOrWhiteSpace(localIp) ||
            !IPAddress.TryParse(localIp.Trim(), out IPAddress? host) ||
            host.AddressFamily != AddressFamily.InterNetwork)
        {
            return new GvcpScanResult(Array.Empty<GvcpDeviceInfo>(), "Invalid local NIC IPv4: " + localIp);
        }

        var devices = new List<GvcpDeviceInfo>();
        string error = string.Empty;

        try
        {
            using var client = new UdpClient(new IPEndPoint(host, 0));
            client.EnableBroadcast = true;

            // GVCP DISCOVERY_CMD 0x0002, request ID 1. No camera settings change.
            byte[] frame = { 0x42, 0x11, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01 };

            // 1. Broadcast discovery on camera subnet
            await client.SendAsync(frame, frame.Length, new IPEndPoint(IPAddress.Broadcast, 3956));

            // 2. Unicast discovery to typed target IP if provided
            if (!string.IsNullOrWhiteSpace(targetIp) &&
                IPAddress.TryParse(targetIp.Trim(), out IPAddress? target) &&
                target.AddressFamily == AddressFamily.InterNetwork)
            {
                try
                {
                    await client.SendAsync(frame, frame.Length, new IPEndPoint(target, 3956));
                }
                catch
                {
                    // Unicast send failure is non-fatal; broadcast already went out
                }
            }

            using var stop = new CancellationTokenSource(timeoutMs);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var response = await client.ReceiveAsync(stop.Token);
                    var info = ParseDiscoveryAck(response.Buffer, response.RemoteEndPoint.Address);
                    if (info is not null)
                    {
                        string dedupeKey = !string.IsNullOrWhiteSpace(info.SerialNumber)
                            ? info.SerialNumber
                            : info.Address;

                        if (seen.Add(dedupeKey))
                        {
                            devices.Add(info);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { break; }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return new GvcpScanResult(devices, error);
    }

    /// <summary>Backward-compatible helper returning discovered addresses.</summary>
    public static async Task<IReadOnlyList<string>> ScanAsync(string hostIp, int timeoutMs = 1000)
    {
        var result = await DiscoverAsync(hostIp, null, timeoutMs);
        return result.Devices.Where(d => d.IsIdentified).Select(d => d.Address).ToList();
    }

    private static string ReadAscii(byte[] data, int start, int length)
    {
        if (data is null || start < 0 || start >= data.Length) return string.Empty;
        int count = Math.Min(length, data.Length - start);
        if (count <= 0) return string.Empty;
        int zero = Array.IndexOf(data, (byte)0, start, count);
        int textLength = zero >= 0 ? zero - start : count;
        return System.Text.Encoding.ASCII.GetString(data, start, textLength).Trim();
    }
}
