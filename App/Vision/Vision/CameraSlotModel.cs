using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PlateVision50.Vision;

/// <summary>Operator-selected transport mode for one camera slot.</summary>
public enum CameraTransportMode
{
    Usb,
    Ip
}

/// <summary>Station defaults and legacy persistence migration; Auto no longer exists.</summary>
public static class CameraSlotDefaults
{
    /// <summary>Convenience default only: station 1 starts in IP, station 2 in USB; either can switch.</summary>
    public static CameraTransportMode DefaultMode(int slot) =>
        slot == 1 ? CameraTransportMode.Ip : CameraTransportMode.Usb;

    /// <summary>Saved USB/IP survive; legacy "Auto", null, blank and invalid values become the station default.</summary>
    public static CameraTransportMode MigrateMode(string? saved, int slot)
    {
        string value = (saved ?? string.Empty).Trim();

        if (value.Equals("Usb", StringComparison.OrdinalIgnoreCase)) return CameraTransportMode.Usb;
        if (value.Equals("Ip", StringComparison.OrdinalIgnoreCase)) return CameraTransportMode.Ip;

        return DefaultMode(slot);
    }
}

/// <summary>One vendor/transport scan failure, kept beside the devices that did enumerate.</summary>
public sealed record CameraScanIssue(
    string Vendor,
    string Transport,
    string ErrorType,
    string Message,
    string Details)
{
    /// <summary>Succinct expander header, e.g. "BASLER SDK ERROR", "HIK SDK ERROR", "GVCP ERROR".</summary>
    public string Label
    {
        get
        {
            string vendor = (Vendor ?? string.Empty).Trim();

            if (vendor.Contains("HIK", StringComparison.OrdinalIgnoreCase)) return "HIK SDK ERROR";
            if (vendor.Contains("BASLER", StringComparison.OrdinalIgnoreCase)) return "BASLER SDK ERROR";
            if (vendor.Contains("GVCP", StringComparison.OrdinalIgnoreCase)) return "GVCP ERROR";
            if (vendor.Length == 0) return "CAMERA ERROR";

            return vendor.ToUpperInvariant() + " ERROR";
        }
    }

    /// <summary>One-line operator summary; the full diagnostic stays in Details.</summary>
    public string Summary
    {
        get
        {
            string transport = (Transport ?? string.Empty).Trim();
            string type = (ErrorType ?? string.Empty).Trim();
            string message = OneLine(Message);

            string head = Label + (transport.Length == 0 ? string.Empty : " · " + transport.ToUpperInvariant());
            if (message.Length == 0) return head;
            if (type.Length == 0) return head + ": " + message;

            return head + ": " + message + " [" + type + "]";
        }
    }

    private static string OneLine(string? text) =>
        (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
}

/// <summary>One scan result: every device that enumerated plus every vendor failure that did not.</summary>
public sealed record CameraScanResult(IReadOnlyList<CameraDevice> Devices, IReadOnlyList<CameraScanIssue> Issues)
{
    public bool HasIssues => Issues is { Count: > 0 };

    public string Summary
    {
        get
        {
            int deviceCount = Devices?.Count ?? 0;

            if (!HasIssues) return deviceCount.ToString(CultureInfo.InvariantCulture) + " DEVICES";

            CameraScanIssue first = Issues![0];
            return deviceCount.ToString(CultureInfo.InvariantCulture) + " DEVICES | " +
                   Issues.Count.ToString(CultureInfo.InvariantCulture) + " ERR: " + first.Summary;
        }
    }
}

/// <summary>Truthful camera slot lifecycle; no state implies an image without a received frame.</summary>
public enum CameraSlotState
{
    Offline,
    Scanning,
    Connecting,
    Connected,
    Live,
    Lost,
    Error
}

/// <summary>One camera discovered by a vendor SDK scan (USB or GigE/IP).</summary>
public sealed record CameraDevice(
    string Vendor,
    string Transport,
    string Model,
    string Serial,
    string Address,
    int BackendIndex)
{
    /// <summary>Vendor + "|" + serial, upper-cased so ownership checks are case-insensitive.</summary>
    public string Key => ((Vendor ?? string.Empty).Trim() + "|" + (Serial ?? string.Empty).Trim()).ToUpperInvariant();

    /// <summary>"Model · Serial · Vendor · Transport"</summary>
    public string Display =>
        $"{(Model ?? string.Empty).Trim()} · {(Serial ?? string.Empty).Trim()} · {(Vendor ?? string.Empty).Trim()} · {(Transport ?? string.Empty).Trim()}";

    public override string ToString() => Display;

    /// <summary>True for generic UVC/webcam hits that have no vendor SDK image backend.</summary>
    public bool IsGenericUvc =>
        Mentions(Vendor, "UVC") || Mentions(Vendor, "WEBCAM") || Mentions(Vendor, "GENERIC") ||
        Mentions(Transport, "UVC") || Mentions(Transport, "WEBCAM") || Mentions(Transport, "GENERIC");

    private static bool Mentions(string? text, string token) =>
        !string.IsNullOrWhiteSpace(text) && text.Contains(token, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Process-wide claim table so two station slots never open the same physical camera.</summary>
public sealed class CameraOwnershipRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _owners = new(StringComparer.Ordinal);

    /// <summary>Claims the device for the slot; a slot may always re-claim its own key.</summary>
    public bool TryClaim(string deviceKey, int slot, out string error)
    {
        string key = Normalize(deviceKey);
        if (key.Length == 0)
        {
            error = "Select a camera device";
            return false;
        }

        lock (_gate)
        {
            if (_owners.TryGetValue(key, out int owner) && owner != slot)
            {
                error = "In use by Station " + owner.ToString("D2", CultureInfo.InvariantCulture);
                return false;
            }

            _owners[key] = slot;
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Frees every device claimed by the slot, leaving other slots untouched.</summary>
    public void Release(int slot)
    {
        lock (_gate)
        {
            List<string>? owned = null;
            foreach (KeyValuePair<string, int> pair in _owners)
            {
                if (pair.Value != slot) continue;
                owned ??= new List<string>();
                owned.Add(pair.Key);
            }

            if (owned is null) return;
            foreach (string key in owned) _owners.Remove(key);
        }
    }

    /// <summary>Slot number holding the device, or null when unclaimed.</summary>
    public int? OwnerOf(string deviceKey)
    {
        string key = Normalize(deviceKey);
        if (key.Length == 0) return null;

        lock (_gate)
            return _owners.TryGetValue(key, out int slot) ? (int?)slot : null;
    }

    /// <summary>Clears every claim, used when the camera backend restarts.</summary>
    public void Reset()
    {
        lock (_gate) _owners.Clear();
    }

    private static string Normalize(string? deviceKey) =>
        (deviceKey ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>Vendor/transport rules: a camera is only dispatchable through its vendor SDK transport.</summary>
public static class CameraTransportPolicy
{
    private static readonly Regex HostNamePattern = new(
        @"^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Hikvision/Basler over USB or GigE only; generic UVC and other vendors have no backend.</summary>
    public static bool IsSupported(CameraDevice device)
    {
        if (device is null || device.IsGenericUvc)
            return false;

        return IsKnownVendor(device.Vendor) && (IsUsb(device.Transport) || IsGigE(device.Transport));
    }

    /// <summary>Actionable reason for an unsupported device; empty when the device is supported.</summary>
    public static string UnsupportedReason(CameraDevice device)
    {
        if (device is null)
            return "Select a camera device";

        if (device.IsGenericUvc)
        {
            return "Unsupported UVC/webcam device '" + device.Display +
                   "': no image capture backend is available for generic UVC devices; use a Hikvision or Basler camera.";
        }

        string vendor = (device.Vendor ?? string.Empty).Trim();
        string transport = (device.Transport ?? string.Empty).Trim();

        if (!IsKnownVendor(vendor))
        {
            return "Unsupported camera vendor '" + (vendor.Length == 0 ? "unknown" : vendor) +
                   "': only Hikvision and Basler cameras have an image capture backend.";
        }

        if (!IsUsb(transport) && !IsGigE(transport))
        {
            return vendor + " transport '" + (transport.Length == 0 ? "unknown" : transport) +
                   "' is not supported: only USB and GigE transports have an image capture backend.";
        }

        return string.Empty;
    }

    /// <summary>USB accepts only USB transport, IP accepts only GigE/IP transport.</summary>
    public static bool MatchesMode(CameraDevice device, CameraTransportMode mode)
    {
        if (device is null) return false;

        return mode == CameraTransportMode.Usb
            ? IsUsb(device.Transport)
            : IsIp(device.Transport);
    }

    /// <summary>Only IP mode needs an operator-entered address; USB cameras have no IPv4.</summary>
    public static bool RequiresIp(CameraTransportMode mode) => mode == CameraTransportMode.Ip;

    /// <summary>Accepts a dotted-quad IPv4 address or an RFC-1123 hostname; rejects URLs, ports and spaces.</summary>
    public static bool IsValidAddress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();
        if (value.Length == 0 || value.Length > 253) return false;

        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || c == ':' || c == '/' || c == '\\') return false;
        }

        if (IsNumericDotted(value)) return IsIpv4(value);

        if (value[0] == '.' || value[value.Length - 1] == '.') return false;
        if (!HostNamePattern.IsMatch(value)) return false;

        foreach (string label in value.Split('.'))
        {
            if (label.Length == 0 || label.Length > 63) return false;
        }

        return true;
    }

    /// <summary>True when the device is supported and its transport matches the requested mode.</summary>
    public static bool CanDispatch(CameraDevice device, CameraTransportMode mode, out string reason)
    {
        if (device is null)
        {
            reason = "Select a camera device";
            return false;
        }

        if (!IsSupported(device))
        {
            reason = UnsupportedReason(device);
            return false;
        }

        if (!CanDispatchManual(device, out string manualReason))
        {
            reason = manualReason;
            return false;
        }

        if (mode == CameraTransportMode.Usb && !IsUsb(device.Transport))
        {
            reason = Name(device) + " camera cannot be dispatched in USB mode: only USB transport is allowed; switch to IP mode or select a USB device.";
            return false;
        }

        if (mode == CameraTransportMode.Ip && !IsGigE(device.Transport))
        {
            reason = Name(device) + " camera cannot be dispatched in IP mode: only GigE transport is allowed; switch to USB mode or select a GigE device.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Hikvision and its Hikrobot product branding share one MVS backend.</summary>
    public static bool IsHikVendor(string? vendor)
    {
        string value = (vendor ?? string.Empty).Trim();

        return value.Equals("Hikvision", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("Hikrobot", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A device with no SDK backend (BackendIndex below zero) is only reachable through the
    /// Hikrobot direct-IP path. Basler direct IP is not implemented and needs a real scan.
    /// </summary>
    public static bool CanDispatchManual(CameraDevice device, out string reason)
    {
        if (device is null || device.BackendIndex >= 0)
        {
            reason = string.Empty;
            return true;
        }

        if (IsHikVendor(device.Vendor))
        {
            reason = string.Empty;
            return true;
        }

        reason = Name(device) + " typed-IP connect is not supported: SCAN REQUIRED. " +
                 "Only Hikrobot/Hikvision cameras can be opened directly by IP; select a scanned device instead.";
        return false;
    }

    private static bool IsKnownVendor(string? vendor) =>
        string.Equals((vendor ?? string.Empty).Trim(), "Basler", StringComparison.OrdinalIgnoreCase) ||
        IsHikVendor(vendor);

    private static bool IsUsb(string? transport) =>
        !string.IsNullOrWhiteSpace(transport) && transport.Contains("USB", StringComparison.OrdinalIgnoreCase);

    private static bool IsGigE(string? transport) =>
        !string.IsNullOrWhiteSpace(transport) && transport.Contains("GIGE", StringComparison.OrdinalIgnoreCase);

    private static bool IsIp(string? transport) =>
        IsGigE(transport) ||
        (!string.IsNullOrWhiteSpace(transport) && transport.Contains("IP", StringComparison.OrdinalIgnoreCase));

    private static string Name(CameraDevice device) =>
        (string.IsNullOrWhiteSpace(device.Vendor) ? "Unknown vendor" : device.Vendor.Trim()) + " " +
        (string.IsNullOrWhiteSpace(device.Transport) ? "unknown transport" : device.Transport.Trim());

    private static bool IsNumericDotted(string value)
    {
        bool hasDigit = false;
        foreach (char c in value)
        {
            if (c == '.') continue;
            if (!char.IsAsciiDigit(c)) return false;
            hasDigit = true;
        }

        return hasDigit;
    }

    private static bool IsIpv4(string value)
    {
        string[] parts = value.Split('.');
        if (parts.Length != 4) return false;

        foreach (string part in parts)
        {
            if (part.Length == 0 || part.Length > 3) return false;

            int octet = 0;
            foreach (char c in part)
            {
                if (!char.IsAsciiDigit(c)) return false;
                octet = (octet * 10) + (c - '0');
            }

            if (octet > 255) return false;
        }

        return true;
    }
}

/// <summary>One station camera slot: device selection, mode filtering, truthful state and ownership validation.</summary>
public sealed class CameraSlotModel
{
    private readonly List<CameraDevice> _devices = new();
    private readonly List<CameraDevice> _filtered = new();
    private readonly List<CameraScanIssue> _issues = new();
    private readonly ReadOnlyCollection<CameraDevice> _devicesView;
    private readonly ReadOnlyCollection<CameraDevice> _filteredView;
    private readonly ReadOnlyCollection<CameraScanIssue> _issuesView;
    private CameraTransportMode _mode;
    private string _ipText = string.Empty;
    private CameraDevice? _selected;

    public CameraSlotModel(int slotNumber, string name)
    {
        SlotNumber = slotNumber;
        Name = name ?? string.Empty;
        _mode = CameraSlotDefaults.DefaultMode(slotNumber);
        _devicesView = _devices.AsReadOnly();
        _filteredView = _filtered.AsReadOnly();
        _issuesView = _issues.AsReadOnly();
    }

    public int SlotNumber { get; }
    public string Name { get; }

    public CameraTransportMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;

            _mode = value;
            ScanGeneration++;

            if (_selected is not null && !CameraTransportPolicy.MatchesMode(_selected, _mode))
                _selected = null;

            RefreshFiltered();
        }
    }

    /// <summary>Bumped on every mode change so a scan that finished after the switch is discarded.</summary>
    public int ScanGeneration { get; private set; }

    public string IpText
    {
        get => _ipText;
        set => _ipText = value ?? string.Empty;
    }

    public CameraSlotState State { get; private set; } = CameraSlotState.Offline;
    public string StatusText { get; private set; } = "OFFLINE";
    public string LastError { get; private set; } = string.Empty;
    public DateTime LastFrameUtc { get; private set; }

    public IReadOnlyList<CameraDevice> Devices => _devicesView;
    public IReadOnlyList<CameraDevice> FilteredDevices => _filteredView;
    public IReadOnlyList<CameraScanIssue> ScanIssues => _issuesView;
    public CameraDevice? Selected => _selected;

    /// <summary>
    /// Short operator label. Connected shows CONNECTED until a frame arrives and NO FRAME once a
    /// previously received frame is no longer current, so the label never claims a live picture.
    /// </summary>
    public string DisplayState => State switch
    {
        CameraSlotState.Scanning => "SCANNING",
        CameraSlotState.Connecting => "CONNECTING",
        CameraSlotState.Connected => LastFrameUtc == default ? "CONNECTED" : "NO FRAME",
        CameraSlotState.Live => "LIVE",
        CameraSlotState.Lost => "LOST",
        CameraSlotState.Error => "ERROR",
        _ => "OFFLINE"
    };

    /// <summary>Replaces the scan devices, clearing any previous scan issues.</summary>
    public void SetDevices(IEnumerable<CameraDevice> devices)
    {
        _issues.Clear();
        ApplyDevices(devices);
    }

    /// <summary>Applies one scan result: devices survive partial SDK failures and every issue is kept.</summary>
    public void SetScanResult(CameraScanResult result)
    {
        _issues.Clear();

        if (result?.Issues is not null)
        {
            foreach (CameraScanIssue issue in result.Issues)
            {
                if (issue is not null) _issues.Add(issue);
            }
        }

        ApplyDevices(result?.Devices);
    }

    private void ApplyDevices(IEnumerable<CameraDevice>? devices)
    {
        _devices.Clear();

        if (devices is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CameraDevice device in devices)
            {
                if (device is null || !seen.Add(device.Key)) continue;
                _devices.Add(device);
            }
        }

        _devices.Sort(CompareDevices);

        if (_selected is not null)
        {
            CameraDevice? match = null;
            foreach (CameraDevice device in _devices)
            {
                if (!SameKey(device.Key, _selected.Key)) continue;
                match = device;
                break;
            }

            _selected = match;
        }

        RefreshFiltered();
        StatusText = "SCAN " + _devices.Count.ToString(CultureInfo.InvariantCulture) + " DEVICES | USB "
                   + CountTransport(CameraTransportMode.Usb).ToString(CultureInfo.InvariantCulture) + " | IP "
                   + CountTransport(CameraTransportMode.Ip).ToString(CultureInfo.InvariantCulture)
                   + (_issues.Count > 0
                       ? " | ERR " + _issues.Count.ToString(CultureInfo.InvariantCulture)
                       : string.Empty);
    }

    /// <summary>Selects a scanned device only when it matches the current mode and is supported.</summary>
    public bool SelectDevice(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            LastError = "Select a camera device";
            return false;
        }

        CameraDevice? match = null;
        foreach (CameraDevice device in _devices)
        {
            if (!SameKey(device.Key, key)) continue;
            match = device;
            break;
        }

        if (match is null)
        {
            LastError = "Camera device is not in the scanned list";
            return false;
        }

        if (!CameraTransportPolicy.MatchesMode(match, Mode))
        {
            LastError = "Selected device does not match USB/IP mode";
            return false;
        }

        if (!CameraTransportPolicy.IsSupported(match))
        {
            LastError = CameraTransportPolicy.UnsupportedReason(match);
            return false;
        }

        if (!CameraTransportPolicy.CanDispatchManual(match, out string manualReason))
        {
            LastError = manualReason;
            return false;
        }

        _selected = match;
        LastError = string.Empty;
        return true;
    }

    /// <summary>Validates mode, support, IP text (IP mode only) and ownership before any SDK open.</summary>
    public (bool ok, string error) ValidateBeforeConnect(CameraOwnershipRegistry registry)
    {
        if (_selected is null)
            return (false, "Select a camera device");

        bool matchesMode = false;
        foreach (CameraDevice device in _filtered)
        {
            if (!SameKey(device.Key, _selected.Key)) continue;
            matchesMode = true;
            break;
        }

        if (!matchesMode)
            return (false, "Selected device does not match USB/IP mode");

        if (!CameraTransportPolicy.IsSupported(_selected))
            return (false, CameraTransportPolicy.UnsupportedReason(_selected));

        if (!CameraTransportPolicy.CanDispatchManual(_selected, out string directReason))
            return (false, directReason);

        if (Mode == CameraTransportMode.Ip && !CameraTransportPolicy.IsValidAddress(IpText))
            return (false, "Enter a valid IPv4 address or hostname");

        if (registry is null)
            return (false, "Camera ownership registry is unavailable");

        return registry.TryClaim(_selected.Key, SlotNumber, out string error)
            ? (true, string.Empty)
            : (false, error);
    }

    public void MarkScanning()
    {
        State = CameraSlotState.Scanning;
        LastError = string.Empty;
        StatusText = "SCANNING";
    }

    /// <summary>Scan finished without connecting: stay offline but keep the scan summary and issues.</summary>
    public void MarkScanComplete()
    {
        State = CameraSlotState.Offline;
        LastError = string.Empty;
    }

    public void MarkConnecting()
    {
        State = CameraSlotState.Connecting;
        LastError = string.Empty;
        StatusText = "CONNECTING";
    }

    public void MarkConnected()
    {
        State = CameraSlotState.Connected;
        LastError = string.Empty;
        StatusText = "CONNECTED";
    }

    public void MarkFrame(DateTime utcNow)
    {
        LastFrameUtc = utcNow;
        State = CameraSlotState.Live;
        LastError = string.Empty;
        StatusText = "LIVE";
    }

    public void MarkLost(string reason)
    {
        string detail = string.IsNullOrWhiteSpace(reason) ? "Frame stream lost" : reason.Trim();
        State = CameraSlotState.Lost;
        LastError = detail;
        StatusText = "LOST: " + detail;
    }

    public void MarkError(string reason)
    {
        string detail = string.IsNullOrWhiteSpace(reason) ? "Camera error" : reason.Trim();
        State = CameraSlotState.Error;
        LastError = detail;
        StatusText = "ERROR: " + detail;
    }

    public void MarkOffline()
    {
        State = CameraSlotState.Offline;
        LastError = string.Empty;
        StatusText = "OFFLINE";
    }

    /// <summary>LIVE only while the state is Live and the last frame is within the timeout window.</summary>
    public bool IsLive(DateTime utcNow, TimeSpan timeout)
    {
        if (State != CameraSlotState.Live || timeout <= TimeSpan.Zero) return false;

        TimeSpan age = utcNow - LastFrameUtc;
        return age >= TimeSpan.Zero && age <= timeout;
    }

    private void RefreshFiltered()
    {
        _filtered.Clear();
        foreach (CameraDevice device in _devices)
        {
            if (CameraTransportPolicy.MatchesMode(device, _mode)) _filtered.Add(device);
        }
    }

    private int CountTransport(CameraTransportMode mode)
    {
        int count = 0;
        foreach (CameraDevice device in _devices)
        {
            if (CameraTransportPolicy.MatchesMode(device, mode)) count++;
        }

        return count;
    }

    private static int CompareDevices(CameraDevice a, CameraDevice b)
    {
        int result = Rank(a).CompareTo(Rank(b));
        if (result != 0) return result;

        result = string.Compare(a.Model, b.Model, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;
        result = string.Compare(a.Serial, b.Serial, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;
        result = string.Compare(a.Vendor, b.Vendor, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;
        result = string.Compare(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);
        if (result != 0) return result;
        return a.BackendIndex.CompareTo(b.BackendIndex);
    }

    private static int Rank(CameraDevice device)
    {
        if (CameraTransportPolicy.MatchesMode(device, CameraTransportMode.Usb)) return 0;
        if (CameraTransportPolicy.MatchesMode(device, CameraTransportMode.Ip)) return 1;
        return 2;
    }

    private static bool SameKey(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
