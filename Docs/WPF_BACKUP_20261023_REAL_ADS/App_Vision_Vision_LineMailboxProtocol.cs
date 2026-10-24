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
}
