using System;
using System.Collections.Generic;
using System.Linq;

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
/// Single-writer gateway for all WPF -> PLC command interactions.
/// Enforces commissioning gates, fails closed on missing symbols or offline ADS,
/// and prevents competing PLC writers across MainWindow and child views.
/// </summary>
public sealed class LineMailboxGateway
{
    private readonly object _gate = new();
    private uint _sequenceCounter = 0;
    private bool _heartbeatState = false;

    public static LineMailboxGateway Instance { get; } = new();

    public uint CurrentSequence
    {
        get { lock (_gate) return _sequenceCounter; }
    }

    public bool CurrentHeartbeat
    {
        get { lock (_gate) return _heartbeatState; }
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

    public CommandDispatchResult RequestLineStart(
        bool isMockMode,
        bool adsOnline,
        bool symbolsAvailable,
        bool camera1Ready,
        bool tracking1Ready)
    {
        lock (_gate)
        {
            if (isMockMode)
            {
                uint seq = AllocateSequence();
                return new CommandDispatchResult(true, 1, $"TEST ONLY: Simulated line START accepted (seq={seq}, NO PLC WRITE)");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 15, "START BLOCKED: TwinCAT ADS port 851 is offline or stale.");

            if (!camera1Ready || !tracking1Ready)
                return new CommandDispatchResult(false, 13, "START BLOCKED: Camera 1 acquisition and tracking must be verified live first.");

            if (!symbolsAvailable)
                return new CommandDispatchResult(false, 11, "START BLOCKED: PLC HMI mailbox symbols are MISSING on runtime (CONTRACT PENDING).");

            if (!LineMailboxProtocol.ProductionCommandSourceApproved)
                return new CommandDispatchResult(false, 11, "START BLOCKED: Production command source is not commissioned: physical writes disarmed.");

            return new CommandDispatchResult(false, 11, "START BLOCKED: Commissioning gate cLineAutoCommissioned is FALSE in TwinCAT.");
        }
    }

    public CommandDispatchResult RequestLineStop(bool isMockMode, bool adsOnline)
    {
        lock (_gate)
        {
            if (isMockMode)
            {
                return new CommandDispatchResult(true, 1, "TEST ONLY: Simulated line STOP accepted (NO PLC WRITE). NOT a certified E-stop.");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 15, "STOP BLOCKED: ADS 851 is offline. In danger use PHYSICAL EMERGENCY STOP.");

            if (!LineMailboxProtocol.ProductionCommandSourceApproved)
                return new CommandDispatchResult(false, 11, "STOP BLOCKED: PLC mailbox commands not commissioned. In danger use PHYSICAL EMERGENCY STOP.");

            return new CommandDispatchResult(false, 11, "STOP BLOCKED: Controlled stop requires commissioned PLC contract.");
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

            if (!LineMailboxProtocol.ProductionCommandSourceApproved)
                return new CommandDispatchResult(false, 11, "RESET BLOCKED: PLC mailbox commands not commissioned.");

            return new CommandDispatchResult(false, 11, "RESET BLOCKED: Reset requires commissioned PLC contract.");
        }
    }

    public CommandDispatchResult RequestManualBeltStart(bool isMockMode, bool adsOnline, bool speedMapReady, double speedMmS)
    {
        lock (_gate)
        {
            if (isMockMode)
            {
                return new CommandDispatchResult(true, 1, $"TEST ONLY: Simulated manual belt START accepted at {speedMmS:F1} mm/s (NO PLC WRITE).");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 12, "MANUAL START BLOCKED: ADS 851 is offline.");

            if (!speedMapReady)
                return new CommandDispatchResult(false, 13, "MANUAL START BLOCKED: Belt speed map is NOT ready (calibration pending).");

            if (!LineMailboxProtocol.ProductionCommandSourceApproved)
                return new CommandDispatchResult(false, 11, "MANUAL START BLOCKED: Production command source is not commissioned: physical writes disarmed.");

            return new CommandDispatchResult(false, 11, "MANUAL START BLOCKED: Commissioning gate cBeltMmSCommissioned is FALSE in TwinCAT.");
        }
    }

    public CommandDispatchResult RequestManualBeltStop(bool isMockMode, bool adsOnline)
    {
        lock (_gate)
        {
            if (isMockMode)
            {
                return new CommandDispatchResult(true, 1, "TEST ONLY: Simulated manual belt STOP accepted (NO PLC WRITE).");
            }

            if (!adsOnline)
                return new CommandDispatchResult(false, 12, "MANUAL STOP BLOCKED: ADS 851 is offline. In danger use PHYSICAL EMERGENCY STOP.");

            if (!LineMailboxProtocol.ProductionCommandSourceApproved)
                return new CommandDispatchResult(false, 11, "MANUAL STOP BLOCKED: Production command source is not commissioned. In danger use PHYSICAL EMERGENCY STOP.");

            return new CommandDispatchResult(false, 11, "MANUAL STOP BLOCKED: Controlled stop requires commissioned PLC contract.");
        }
    }
}
