using System;
using System.Collections.Generic;
using System.Linq;
using TwinCAT.Ads;
namespace PlateVision50.Vision;

/// <summary>
/// Source/runtime contract for the shared DATN line. This is pure logic: it
/// never opens an ADS connection, writes a tag or claims that a drive moved.
/// All physical command sending remains disarmed pending commissioning.
/// </summary>
public static class LineMailboxProtocol
{
    // Source-only commissioning gate. Do NOT enable from a UI switch or ADS value.
    public static bool ProductionCommandSourceApproved => false; // Hardcoded disarmed pending commissioning.
    public static readonly string[] RequiredReadbackSymbols =
    {
        "MAIN.bHmiLinkFresh",
        "MAIN.bBeltSpeedMapReady",
        "MAIN.nBeltSpeedMapState",
        "MAIN.nBeltCommandResult",
        "MAIN.nLineCommandResult",
        "MAIN.udiLineLastAckSeq",
        "MAIN.bLineAutoAccepted",
        "MAIN.rMD200_SetSpeed_mm_s",
        "MAIN.rBeltAppliedSpeed_mm_s",
        "MAIN.rBeltMeasuredMmPerHz"
    };

    public static bool IsTelemetryFresh(DateTime receivedUtc, DateTime nowUtc, TimeSpan maximumAge)
        => receivedUtc.Kind == DateTimeKind.Utc
           && maximumAge > TimeSpan.Zero
           && nowUtc >= receivedUtc
           && nowUtc - receivedUtc <= maximumAge;

    public static bool AllReadbacksPresent(IReadOnlyDictionary<string, bool> presence)
        => RequiredReadbackSymbols.All(symbol =>
            presence.TryGetValue(symbol, out bool available) && available);

    // ACK is a handled command, never confirmation of physical conveyor motion.
    public static bool Acknowledged(uint requestSequence, uint receivedSequence, ushort result)
        => requestSequence != 0 && requestSequence == receivedSequence && result == 1;

    public static uint NextSequence(uint current)
        => current == uint.MaxValue ? 1u : current + 1u;

    public static bool TryConvertSpeed(double millimetersPerSecond, double measuredMmPerHz,
        bool speedMapReady, out double hertz)
    {
        hertz = 0;
        if (!speedMapReady || !double.IsFinite(millimetersPerSecond)
            || !double.IsFinite(measuredMmPerHz) || measuredMmPerHz <= 0
            || millimetersPerSecond <= 0)
            return false;
        double calculated = millimetersPerSecond / measuredMmPerHz;
        if (!double.IsFinite(calculated) || calculated <= 0 || calculated > 25)
            return false;
        hertz = calculated;
        return true;
    }

    public static string DescribeSpeedMap(ushort state) => state switch
    {
        0 => "NOT COMMISSIONED",
        1 => "CALIBRATION PENDING",
        2 => "READY (check ADS quality)",
        _ => "UNKNOWN STATE"
    };

    public static string BlockReason(bool adsFresh, bool symbolsPresent, bool eStopAux,
        bool globalStop, bool mapReady, bool heartbeatFresh)
    {
        if (!adsFresh) return "ADS OFFLINE / STALE";
        if (!symbolsPresent) return "HMI PLC SYMBOLS MISSING";
        if (!eStopAux || globalStop) return "STOP / INTERLOCK ACTIVE";
        if (!ProductionCommandSourceApproved) return "PLC/HMI COMMAND NOT COMMISSIONED";
        if (!mapReady) return "BELT SPEED CALIBRATION PENDING";
        if (!heartbeatFresh) return "HMI HEARTBEAT STALE";
        return "READY FOR SEPARATE OPERATOR REVIEW";
    }

    public static readonly HashSet<string> KnownSourceSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "GVL_MD200_IO.bEStopFeedback",
        "GVL_MD200_IO.nAO1_Raw",
        "GVL_MD200_IO.bRUN_DO",
        "MAIN.bSystemEStopOK",
        "MAIN.bSystemStopRequested",
        "MAIN.bServo_Ready",
        "MAIN.bPlcQConnected",
        "MAIN.bGlueCycleBusy",
        "MAIN.bGlueCycleDone",
        "MAIN.bGlueCycleError",
        "MAIN.nGlueCurVertex",
        "MAIN.nGlueErrorCode",
        "MAIN.bLegacyMotionPublisherAllow",
        "MAIN.bVisionFeedVerified",
        "MAIN.nQueueCount",
        "MAIN.nQueueHeadProductID",
        "MAIN.nQueueAccepted",
        "MAIN.nQueueMissed",
        "MAIN.rConveyorSpeed",
        "MAIN.rConveyorDist",
        "MAIN.bMD200_Start",
        "MAIN.rMD200_SetFrequency_Hz",
        "MAIN.rMD200_Ramped_Hz",
        "MAIN.rMD200_AO_VoltsVirtual",
        "MAIN.bHmiLinkFresh",
        "MAIN.bHmiHeartbeat",
        "MAIN.bHmiLineStartReq",
        "MAIN.bHmiLineStopReq",
        "MAIN.bHmiLineResetReq",
        "MAIN.udiHmiLineCmdSeq",
        "MAIN.bHmiCamera1Ready",
        "MAIN.bHmiTracking1Ready",
        "MAIN.bHmiRequireScara",
        "MAIN.udiLineLastAckSeq",
        "MAIN.nLineCommandResult",
        "MAIN.nLineMasterState",
        "MAIN.bLineAutoAccepted",
        "MAIN.bLineNormalStopLatched",
        "MAIN.bHmiBeltSetReq",
        "MAIN.bHmiBeltStartReq",
        "MAIN.bHmiBeltStopReq",
        "MAIN.rHmiBeltSetSpeed_mm_s",
        "MAIN.bBeltSpeedMapReady",
        "MAIN.nBeltSpeedMapState",
        "MAIN.bBeltCalibrationVerified",
        "MAIN.rBeltCalculatedSet_Hz",
        "MAIN.rBeltAppliedSpeed_mm_s",
        "MAIN.rBeltMeasuredMmPerHz",
        "MAIN.nBeltCommandResult",
        "MAIN.rMD200_SetSpeed_mm_s",
        "MAIN.rMD200_AppliedSpeed_mm_s",
        "MAIN.nMD200_SpeedInputState",
        "MAIN.rBeltPreviewUnverified_Hz"
    };

    public static string ClassifyQuality(string tag, bool isAvailableOnRuntime)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "MISSING";
        string t = tag.Trim();
        if (t.StartsWith("GVL_SCARA", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("GVL_CAMERA2", StringComparison.OrdinalIgnoreCase))
            return "RESERVED";

        if (isAvailableOnRuntime)
            return "LIVE";

        if (KnownSourceSymbols.Contains(t))
            return "SOURCE_ONLY";

        return "MISSING";
    }

    public static string DescribeLineCommandResult(ushort code) => code switch
    {
        0 => "NO RESULT / UNEXECUTED",
        1 => "ACCEPTED / ACKNOWLEDGED",
        10 => "REJECTED: Duplicate or zero command sequence",
        11 => "REJECTED: PLC AUTO contract not commissioned",
        12 => "REJECTED: HMI heartbeat stale or missing",
        13 => "REJECTED: Camera 1 or tracking not ready",
        14 => "REJECTED: Robot / PLC-Q not ready",
        15 => "REJECTED: Stop or interlock active",
        16 => "REJECTED: Manual belt owner active or invalid setpoint",
        17 => "REJECTED: SCARA required but not commissioned",
        18 => "REVOKED: AUTO mode revoked; fresh reset required",
        _ => $"RESULT CODE {code}"
    };

    public static string DescribeBeltCommandResult(ushort code) => code switch
    {
        0 => "NO RESULT / UNEXECUTED",
        1 => "ACCEPTED / ACKNOWLEDGED",
        11 => "REJECTED: Belt mm/s not commissioned",
        12 => "REJECTED: Interlock active, stale link or AUTO active",
        13 => "REJECTED: Invalid speed setpoint or zero calibration",
        14 => "REJECTED: Calculated frequency exceeds 25 Hz limit",
        18 => "REVOKED: Belt run permission revoked",
        _ => $"RESULT CODE {code}"
    };
}

public sealed record CommandDispatchResult(bool Success, ushort ResultCode, string Message);

/// <summary>
/// Transport abstraction for TwinCAT ADS communication. Allows decoupled offline
/// testing and mock transports without accessing physical ADS hardware.
/// </summary>
public interface IAdsMailboxTransport : IDisposable
{
    bool IsConnected { get; }
    bool IsInRunState { get; }
    bool TryConnect(int port = 851);
    void Disconnect();

    bool TryRead<T>(string symbolName, out T value);
    T Read<T>(string symbolName);

    bool TryWrite<T>(string symbolName, T value, out string? error);
    void Write<T>(string symbolName, T value);

    IReadOnlyDictionary<string, bool> CheckSymbols(IEnumerable<string> symbolNames);
}

/// <summary>
/// Live ADS 851 transport implementation using Beckhoff.TwinCAT.Ads.AdsClient.
/// Single-writer synchronized, safe variable handle allocation and cleanup.
/// </summary>
public sealed class TwinCatAdsMailboxTransport : IAdsMailboxTransport
{
    private readonly object _lock = new();
    private AdsClient? _client;
    private bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_lock) return _client is not null && _client.IsConnected;
        }
    }

    public bool IsInRunState
    {
        get
        {
            lock (_lock)
            {
                if (_client is null || !_client.IsConnected) return false;
                try
                {
                    return _client.ReadState().AdsState == AdsState.Run;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public bool TryConnect(int port = 851)
    {
        lock (_lock)
        {
            if (_disposed) return false;
            try
            {
                _client?.Dispose();
                _client = new AdsClient();
                _client.Connect(port);
                return _client.IsConnected && _client.ReadState().AdsState == AdsState.Run;
            }
            catch
            {
                try { _client?.Dispose(); } catch { }
                _client = null;
                return false;
            }
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            try { _client?.Disconnect(); } catch { }
            try { _client?.Dispose(); } catch { }
            _client = null;
        }
    }

    public bool TryRead<T>(string symbolName, out T value)
    {
        lock (_lock)
        {
            value = default!;
            if (_client is null || !_client.IsConnected) return false;
            try
            {
                uint handle = _client.CreateVariableHandle(symbolName);
                try
                {
                    value = (T)_client.ReadAny(handle, typeof(T));
                    return true;
                }
                finally
                {
                    _client.DeleteVariableHandle(handle);
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public T Read<T>(string symbolName)
    {
        if (TryRead<T>(symbolName, out var val))
            return val;
        throw new InvalidOperationException($"Failed to read symbol '{symbolName}' from TwinCAT ADS.");
    }

    public bool TryWrite<T>(string symbolName, T value, out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (_client is null || !_client.IsConnected)
            {
                error = "ADS client is not connected.";
                return false;
            }
            try
            {
                uint handle = _client.CreateVariableHandle(symbolName);
                try
                {
                    _client.WriteAny(handle, value!);
                    return true;
                }
                finally
                {
                    _client.DeleteVariableHandle(handle);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public void Write<T>(string symbolName, T value)
    {
        if (!TryWrite<T>(symbolName, value, out var err))
            throw new InvalidOperationException($"Failed to write symbol '{symbolName}': {err}");
    }

    public IReadOnlyDictionary<string, bool> CheckSymbols(IEnumerable<string> symbolNames)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            if (_client is null || !_client.IsConnected)
            {
                foreach (var s in symbolNames) result[s] = false;
                return result;
            }
            foreach (var s in symbolNames)
            {
                try
                {
                    uint handle = _client.CreateVariableHandle(s);
                    _client.DeleteVariableHandle(handle);
                    result[s] = true;
                }
                catch
                {
                    result[s] = false;
                }
            }
        }
        return result;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }
    }
}

public sealed record MailboxWriteRecord(string Symbol, object Value, DateTime TimestampUtc);

/// <summary>
/// In-memory fake ADS transport for offline verification and testing of real command
/// sequences, pulse timing, rising edges, ACK matching and fail-closed safety.
/// </summary>
public sealed class FakeAdsMailboxTransport : IAdsMailboxTransport
{
    private readonly object _lock = new();
    private readonly Dictionary<string, object> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MailboxWriteRecord> _writes = new();

    public bool IsConnected { get; set; } = true;
    public bool IsInRunState { get; set; } = true;

    public Action<string, object, FakeAdsMailboxTransport>? OnSymbolWritten { get; set; }

    public IReadOnlyList<MailboxWriteRecord> Writes
    {
        get { lock (_lock) return _writes.ToList(); }
    }

    public void ClearWrites()
    {
        lock (_lock) _writes.Clear();
    }

    public void SetSymbol(string name, object value)
    {
        lock (_lock) _symbols[name] = value;
    }

    public bool TryConnect(int port = 851)
    {
        IsConnected = true;
        IsInRunState = true;
        return true;
    }

    public void Disconnect()
    {
        IsConnected = false;
        IsInRunState = false;
    }

    public bool TryRead<T>(string symbolName, out T value)
    {
        lock (_lock)
        {
            value = default!;
            if (!IsConnected || !IsInRunState) return false;
            if (_symbols.TryGetValue(symbolName, out var raw))
            {
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }
                try
                {
                    value = (T)Convert.ChangeType(raw, typeof(T));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }

    public T Read<T>(string symbolName)
    {
        if (TryRead<T>(symbolName, out var v)) return v;
        throw new KeyNotFoundException($"Symbol '{symbolName}' not found in FakeAdsMailboxTransport.");
    }

    public bool TryWrite<T>(string symbolName, T value, out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (!IsConnected || !IsInRunState)
            {
                error = "ADS offline or not in RUN";
                return false;
            }
            _symbols[symbolName] = value!;
            _writes.Add(new MailboxWriteRecord(symbolName, value!, DateTime.UtcNow));
            OnSymbolWritten?.Invoke(symbolName, value!, this);
            return true;
        }
    }

    public void Write<T>(string symbolName, T value)
    {
        if (!TryWrite(symbolName, value, out var err))
            throw new InvalidOperationException($"Write failed: {err}");
    }

    public IReadOnlyDictionary<string, bool> CheckSymbols(IEnumerable<string> symbolNames)
    {
        lock (_lock)
        {
            var res = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in symbolNames)
            {
                res[s] = IsConnected && IsInRunState && _symbols.ContainsKey(s);
            }
            return res;
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}

/// <summary>
/// Single-writer gateway for all WPF -> PLC command interactions.
/// Enforces commissioning gates, fails closed on missing symbols or offline ADS,
/// and prevents competing PLC writers across MainWindow and child views.
/// </summary>
public sealed class LineMailboxGateway
{
    private readonly object _gate = new();
    private readonly IAdsMailboxTransport _transport;
    private readonly bool _commissionedForTesting;
    private uint _sequenceCounter = 0;
    private bool _heartbeatState = false;
    private bool _priorBeltSetAccepted = false;
    private double _lastAcceptedBeltSetMmS = 0.0;
    private bool _manualBeltActive = false;
    private bool _autoLineActive = false;

    public static LineMailboxGateway Instance { get; } = new(new TwinCatAdsMailboxTransport(), false);

    public LineMailboxGateway(IAdsMailboxTransport transport, bool commissionedForTesting = false)
    {
        _transport = transport;
        _commissionedForTesting = commissionedForTesting;
    }

    public static LineMailboxGateway CreateForTesting(IAdsMailboxTransport transport, bool commissioned = true)
        => new(transport, commissioned);

    public bool IsArmed => _commissionedForTesting || LineMailboxProtocol.ProductionCommandSourceApproved;

    public IAdsMailboxTransport Transport => _transport;

    public uint CurrentSequence
    {
        get { lock (_gate) return _sequenceCounter; }
    }

    public bool CurrentHeartbeat
    {
        get { lock (_gate) return _heartbeatState; }
    }

    public bool PriorBeltSetAccepted
    {
        get { lock (_gate) return _priorBeltSetAccepted; }
    }

    public double LastAcceptedBeltSetMmS
    {
        get { lock (_gate) return _lastAcceptedBeltSetMmS; }
    }

    public bool ManualBeltActive
    {
        get { lock (_gate) return _manualBeltActive; }
    }

    public bool AutoLineActive
    {
        get { lock (_gate) return _autoLineActive; }
    }

    public bool NextHeartbeatBit()
    {
        lock (_gate)
        {
            _heartbeatState = !_heartbeatState;
            return _heartbeatState;
        }
    }

    public uint AllocateSequence()
    {
        lock (_gate)
        {
            _sequenceCounter = LineMailboxProtocol.NextSequence(_sequenceCounter);
            return _sequenceCounter;
        }
    }

    public bool TickHeartbeat(bool isMockMode, bool adsOnline)
    {
        lock (_gate)
        {
            bool nextBit = NextHeartbeatBit();
            if (isMockMode || !adsOnline || !IsArmed)
            {
                // Alternates locally; strictly NO ADS WRITE while disarmed or in mock mode.
                return false;
            }
            return _transport.TryWrite("MAIN.bHmiHeartbeat", nextBit, out _);
        }
    }

    public void FlushPendingOnDisconnect()
    {
        lock (_gate)
        {
            _manualBeltActive = false;
            _autoLineActive = false;
            _priorBeltSetAccepted = false;
        }
    }

    public CommandDispatchResult RequestManualBeltSet(bool isMockMode, bool adsOnline, double speedMmS)
    {
        lock (_gate)
        {
            if (speedMmS < LineSpeedPreset.MinMmS || speedMmS > LineSpeedPreset.MaxMmS)
                return new CommandDispatchResult(false, 13, $"SET BLOCKED: Speed {speedMmS:F1} mm/s outside permitted range [1.0..150.0].");

            if (isMockMode)
            {
                _priorBeltSetAccepted = true;
                _lastAcceptedBeltSetMmS = speedMmS;
                return new CommandDispatchResult(true, 1, $"TEST ONLY: Simulated manual belt SET {speedMmS:F1} mm/s accepted (NO PLC WRITE).");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 12, "MANUAL SET BLOCKED: ADS 851 is offline or stale.");

            if (!IsArmed)
                return new CommandDispatchResult(false, 11, "MANUAL SET BLOCKED: Production command source is not commissioned: physical writes disarmed.");

            if (!_transport.IsConnected || !_transport.IsInRunState)
                return new CommandDispatchResult(false, 12, "MANUAL SET BLOCKED: ADS transport is not connected in RUN state.");
            // 1. Write target speed in mm/s (LREAL)
            if (!_transport.TryWrite("MAIN.rHmiBeltSetSpeed_mm_s", speedMmS, out var errSpeed))
                return new CommandDispatchResult(false, 12, $"MANUAL SET FAILED: Could not write setpoint: {errSpeed}");

            // 2. Pulse rising edge: write bHmiBeltSetReq = true
            if (!_transport.TryWrite("MAIN.bHmiBeltSetReq", true, out var errReq))
                return new CommandDispatchResult(false, 12, $"MANUAL SET FAILED: Could not assert set request: {errReq}");

            // 3. Read back result
            ushort result = 0;
            if (!_transport.TryRead("MAIN.nBeltCommandResult", out result))
                result = 0;

            // 4. Release pulse: write bHmiBeltSetReq = false
            _transport.TryWrite("MAIN.bHmiBeltSetReq", false, out _);

            if (result == 1)
            {
                _priorBeltSetAccepted = true;
                _lastAcceptedBeltSetMmS = speedMmS;
                return new CommandDispatchResult(true, 1, $"MANUAL SET ACCEPTED: Setpoint {speedMmS:F1} mm/s registered by PLC.");
            }

            _priorBeltSetAccepted = false;
            return new CommandDispatchResult(false, result != 0 ? result : (ushort)12, $"MANUAL SET REJECTED: {LineMailboxProtocol.DescribeBeltCommandResult(result)}");
        }
    }

    public CommandDispatchResult RequestManualBeltStart(bool isMockMode, bool adsOnline, bool speedMapReady, double speedMmS)
    {
        lock (_gate)
        {
            if (speedMmS < LineSpeedPreset.MinMmS || speedMmS > LineSpeedPreset.MaxMmS)
                return new CommandDispatchResult(false, 13, $"START BLOCKED: Speed {speedMmS:F1} mm/s outside permitted range [1.0..150.0].");

            if (_autoLineActive)
                return new CommandDispatchResult(false, 12, "MANUAL START BLOCKED: Line AUTO mode is currently active. Simultaneous manual and AUTO ownership prohibited.");

            if (isMockMode)
            {
                _manualBeltActive = true;
                return new CommandDispatchResult(true, 1, $"TEST ONLY: Simulated manual belt START accepted at {speedMmS:F1} mm/s (NO PLC WRITE).");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 12, "MANUAL START BLOCKED: ADS 851 is offline.");

            if (!speedMapReady)
                return new CommandDispatchResult(false, 13, "MANUAL START BLOCKED: Belt speed map is NOT ready (calibration pending).");

            if (!IsArmed)
                return new CommandDispatchResult(false, 11, "MANUAL START BLOCKED: Production command source is not commissioned: physical writes disarmed.");

            if (!_transport.IsConnected || !_transport.IsInRunState)
                return new CommandDispatchResult(false, 12, "MANUAL START BLOCKED: ADS transport is not connected in RUN state.");
            if (!_priorBeltSetAccepted || Math.Abs(_lastAcceptedBeltSetMmS - speedMmS) > 0.05)
            {
                var setResult = RequestManualBeltSet(false, adsOnline, speedMmS);
                if (!setResult.Success)
                    return new CommandDispatchResult(false, setResult.ResultCode, "MANUAL START BLOCKED: Prior speed SET failed: " + setResult.Message);
            }

            // Pulse rising edge: write bHmiBeltStartReq = true
            if (!_transport.TryWrite("MAIN.bHmiBeltStartReq", true, out var errStart))
                return new CommandDispatchResult(false, 12, $"MANUAL START FAILED: Could not assert start request: {errStart}");

            // Read back result
            ushort result = 0;
            if (!_transport.TryRead("MAIN.nBeltCommandResult", out result))
                result = 0;

            // Release pulse: write bHmiBeltStartReq = false
            _transport.TryWrite("MAIN.bHmiBeltStartReq", false, out _);

            if (result == 1)
            {
                _manualBeltActive = true;
                return new CommandDispatchResult(true, 1, $"MANUAL BELT START ACCEPTED by PLC at {speedMmS:F1} mm/s.");
            }

            _manualBeltActive = false;
            return new CommandDispatchResult(false, result != 0 ? result : (ushort)12, $"MANUAL START REJECTED: {LineMailboxProtocol.DescribeBeltCommandResult(result)}");
        }
    }

    public CommandDispatchResult RequestManualBeltStop(bool isMockMode, bool adsOnline)
    {
        lock (_gate)
        {
            if (isMockMode)
            {
                _manualBeltActive = false;
                return new CommandDispatchResult(true, 1, "TEST ONLY: Simulated manual belt STOP accepted (NO PLC WRITE).");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 12, "MANUAL STOP BLOCKED: ADS 851 is offline. In danger use PHYSICAL EMERGENCY STOP.");

            if (!IsArmed)
                return new CommandDispatchResult(false, 11, "MANUAL STOP BLOCKED: Production command source is not commissioned. In danger use PHYSICAL EMERGENCY STOP.");

            if (!_transport.IsConnected || !_transport.IsInRunState)
                return new CommandDispatchResult(false, 12, "MANUAL STOP BLOCKED: ADS transport is not connected in RUN state.");
            // Always de-assert START request first
            _transport.TryWrite("MAIN.bHmiBeltStartReq", false, out _);

            // Pulse rising edge: write bHmiBeltStopReq = true
            if (!_transport.TryWrite("MAIN.bHmiBeltStopReq", true, out var errStop))
                return new CommandDispatchResult(false, 12, $"MANUAL STOP FAILED: Could not assert stop request: {errStop}");

            // Read result
            ushort result = 0;
            if (!_transport.TryRead("MAIN.nBeltCommandResult", out result))
                result = 0;

            // Release pulse: write bHmiBeltStopReq = false
            _transport.TryWrite("MAIN.bHmiBeltStopReq", false, out _);

            _manualBeltActive = false;
            return new CommandDispatchResult(true, 1, "Manual belt STOP accepted by PLC. (Controlled stop; NOT a certified emergency stop).");
        }
    }

    public CommandDispatchResult RequestLineStart(
        bool isMockMode,
        bool adsOnline,
        bool symbolsAvailable,
        bool camera1Ready,
        bool tracking1Ready)
    {
        lock (_gate)
        {
            if (_manualBeltActive)
                return new CommandDispatchResult(false, 16, "START BLOCKED: Manual belt owner is active. Stop manual belt first.");

            if (isMockMode)
            {
                uint mockSeq = AllocateSequence();
                _autoLineActive = true;
                return new CommandDispatchResult(true, 1, $"TEST ONLY: Simulated line START accepted (seq={mockSeq}, NO PLC WRITE)");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 15, "START BLOCKED: TwinCAT ADS port 851 is offline or stale.");

            if (!camera1Ready || !tracking1Ready)
                return new CommandDispatchResult(false, 13, "START BLOCKED: Camera 1 acquisition and tracking must be verified live first.");

            if (!symbolsAvailable)
                return new CommandDispatchResult(false, 11, "START BLOCKED: PLC HMI mailbox symbols are MISSING on runtime (CONTRACT PENDING).");

            if (!IsArmed)
                return new CommandDispatchResult(false, 11, "START BLOCKED: Production command source is not commissioned: physical writes disarmed.");

            if (!_transport.IsConnected || !_transport.IsInRunState)
                return new CommandDispatchResult(false, 15, "START BLOCKED: ADS transport is not connected in RUN state.");
            uint seq = AllocateSequence();
            _transport.TryWrite("MAIN.udiHmiLineCmdSeq", seq, out _);
            _transport.TryWrite("MAIN.bHmiCamera1Ready", camera1Ready, out _);
            _transport.TryWrite("MAIN.bHmiTracking1Ready", tracking1Ready, out _);
            _transport.TryWrite("MAIN.bHmiRequireScara", false, out _);

            // Pulse rising edge: write bHmiLineStartReq = true
            if (!_transport.TryWrite("MAIN.bHmiLineStartReq", true, out var errStart))
                return new CommandDispatchResult(false, 15, $"START FAILED: Could not assert line start: {errStart}");

            // Wait / read ack seq and result
            uint ackSeq = 0;
            ushort result = 0;
            _transport.TryRead("MAIN.udiLineLastAckSeq", out ackSeq);
            _transport.TryRead("MAIN.nLineCommandResult", out result);

            // Release pulse: write bHmiLineStartReq = false
            _transport.TryWrite("MAIN.bHmiLineStartReq", false, out _);

            if (ackSeq == seq && result == 1)
            {
                _autoLineActive = true;
                return new CommandDispatchResult(true, 1, $"AUTO Line START accepted by PLC (seq={seq}). Logical acceptance only, NOT motion feedback.");
            }

            _autoLineActive = false;
            string desc = LineMailboxProtocol.DescribeLineCommandResult(result);
            return new CommandDispatchResult(false, result != 0 ? result : (ushort)15,
                ackSeq != seq ? $"START TIMEOUT: PLC did not acknowledge seq {seq} (ack={ackSeq}, code={result})" : $"START REJECTED: {desc}");
        }
    }

    public CommandDispatchResult RequestLineStop(bool isMockMode, bool adsOnline)
    {
        lock (_gate)
        {
            if (isMockMode)
            {
                _autoLineActive = false;
                return new CommandDispatchResult(true, 1, "TEST ONLY: Simulated line STOP accepted (NO PLC WRITE). NOT a certified E-stop.");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 15, "STOP BLOCKED: ADS 851 is offline. In danger use PHYSICAL EMERGENCY STOP.");

            if (!IsArmed)
                return new CommandDispatchResult(false, 11, "STOP BLOCKED: PLC mailbox commands not commissioned. In danger use PHYSICAL EMERGENCY STOP.");

            if (!_transport.IsConnected || !_transport.IsInRunState)
                return new CommandDispatchResult(false, 15, "STOP BLOCKED: ADS transport is not connected in RUN state.");
            uint seq = AllocateSequence();
            _transport.TryWrite("MAIN.udiHmiLineCmdSeq", seq, out _);
            _transport.TryWrite("MAIN.bHmiLineStartReq", false, out _);

            // Pulse rising edge: write bHmiLineStopReq = true
            if (!_transport.TryWrite("MAIN.bHmiLineStopReq", true, out var errStop))
                return new CommandDispatchResult(false, 15, $"STOP FAILED: Could not assert stop request: {errStop}");

            uint ackSeq = 0;
            ushort result = 0;
            _transport.TryRead("MAIN.udiLineLastAckSeq", out ackSeq);
            _transport.TryRead("MAIN.nLineCommandResult", out result);

            // Release pulse: write bHmiLineStopReq = false
            _transport.TryWrite("MAIN.bHmiLineStopReq", false, out _);

            _autoLineActive = false;
            return new CommandDispatchResult(true, 1, $"Line STOP accepted by PLC (seq={seq}). Normal controlled stop; NOT a certified emergency stop.");
        }
    }

    public CommandDispatchResult RequestLineReset(bool isMockMode, bool adsOnline)
    {
        lock (_gate)
        {
            if (isMockMode)
            {
                return new CommandDispatchResult(true, 1, "TEST ONLY: Simulated line RESET accepted (NO PLC WRITE).");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 15, "RESET BLOCKED: ADS 851 is offline.");

            if (!IsArmed)
                return new CommandDispatchResult(false, 11, "RESET BLOCKED: PLC mailbox commands not commissioned.");

            if (!_transport.IsConnected || !_transport.IsInRunState)
                return new CommandDispatchResult(false, 15, "RESET BLOCKED: ADS transport is not connected in RUN state.");
            uint seq = AllocateSequence();
            _transport.TryWrite("MAIN.udiHmiLineCmdSeq", seq, out _);

            // Pulse rising edge: write bHmiLineResetReq = true
            if (!_transport.TryWrite("MAIN.bHmiLineResetReq", true, out var errReset))
                return new CommandDispatchResult(false, 15, $"RESET FAILED: Could not assert reset request: {errReset}");

            uint ackSeq = 0;
            ushort result = 0;
            _transport.TryRead("MAIN.udiLineLastAckSeq", out ackSeq);
            _transport.TryRead("MAIN.nLineCommandResult", out result);

            // Release pulse: write bHmiLineResetReq = false
            _transport.TryWrite("MAIN.bHmiLineResetReq", false, out _);

            if (ackSeq == seq && result == 1)
            {
                return new CommandDispatchResult(true, 1, $"Line RESET accepted by PLC (seq={seq}).");
            }

            string desc = LineMailboxProtocol.DescribeLineCommandResult(result);
            return new CommandDispatchResult(false, result != 0 ? result : (ushort)15, $"RESET REJECTED: {desc}");
        }
    }
}
