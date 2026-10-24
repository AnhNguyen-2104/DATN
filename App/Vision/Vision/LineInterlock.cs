using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PlateVision50.Vision;

public enum InterlockState
{
    Offline,
    NotReady,
    Ready,
    StartRequested,
    RunningAcknowledged,
    StopRequested,
    Fault
}

public enum InterlockCheckId
{
    AdsLink,
    Estop,
    PlcQ,
    GantryFault,
    CameraFrame,
    SpeedSetpoint,
    AutoContract,
    ScaraExcluded
}

public sealed record InterlockInputs(
    bool AdsOnline,
    bool EstopOk,
    bool GlobalStop,
    bool CameraConnected,
    bool CameraFrameFresh,
    bool PlcQConnected,
    bool GantryFault,
    bool GantryBusy,
    double PresetSpeedMmS,
    bool AutoContractCommissioned,
    bool ScaraExcluded);

public sealed record InterlockCheck(
    InterlockCheckId Id,
    string Label,
    bool Ok,
    string Detail);

public sealed record InterlockEvaluation(
    InterlockState State,
    bool CanStart,
    bool CanStop,
    bool CanReset,
    string PrimaryReason,
    IReadOnlyList<InterlockCheck> Checks);

public static class LineSpeedPreset
{
    public const double MinMmS = 1.0;
    public const double MaxMmS = 150.0;
    // Matches TwinCAT MAIN.cBeltFixedMmPerHz. Preview only; NOT evidence of field calibration.
    public const double NominalMmPerHz = 6.666666666666667;

    public static string NominalPreviewHz(double mmS)
        => $"Nominal K=6.6667 mm/s/Hz: ~{(mmS / NominalMmPerHz).ToString("F2", CultureInfo.InvariantCulture)} Hz (PREVIEW ONLY - calibration pending; NO PLC WRITE)";

    public static bool TryParse(string? text, out double mmS, out string error)
    {
        mmS = 0.0;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Speed setpoint cannot be blank.";
            return false;
        }

        string trimmed = text.Trim();
        bool parsed = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double val)
                   || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out val);

        if (!parsed || double.IsNaN(val) || double.IsInfinity(val))
        {
            error = $"'{trimmed}' is not a valid numeric speed.";
            return false;
        }

        if (val < MinMmS)
        {
            error = $"Speed {val.ToString("F1", CultureInfo.InvariantCulture)} mm/s is below minimum allowed speed ({MinMmS.ToString("F1", CultureInfo.InvariantCulture)} mm/s).";
            return false;
        }

        if (val > MaxMmS)
        {
            error = $"Speed {val.ToString("F1", CultureInfo.InvariantCulture)} mm/s exceeds maximum allowed speed ({MaxMmS.ToString("F1", CultureInfo.InvariantCulture)} mm/s).";
            return false;
        }

        mmS = val;
        error = string.Empty;
        return true;
    }

    public static string DerivedHzInfo(double mmS, bool calibrationVerified, double mmPerHz)
    {
        if (!calibrationVerified || mmPerHz <= 0.0 || double.IsNaN(mmPerHz) || double.IsInfinity(mmPerHz))
        {
            return "Hz: NOT CALCULATED - mm/s to Hz factor is not verified by measurement.";
        }

        double hz = mmS / mmPerHz;
        return $"Preview ~{hz.ToString("F2", CultureInfo.InvariantCulture)} Hz (calculated, not measured)";
    }
}

public sealed class LineInterlockModel
{
    public InterlockState State { get; private set; } = InterlockState.Offline;
    public bool MockMode { get; set; }

    public InterlockEvaluation Evaluate(InterlockInputs inputs)
    {
        var checks = new List<InterlockCheck>(8);

        // 1. AdsLink
        bool adsOk = inputs.AdsOnline;
        checks.Add(new InterlockCheck(
            InterlockCheckId.AdsLink,
            "ADS Link",
            adsOk,
            adsOk ? "TwinCAT ADS router connected (port 851)" : "ADS 851 OFFLINE: no PLC snapshot; all line commands fail closed."));

        // 2. Estop
        bool estopOk = inputs.AdsOnline && inputs.EstopOk && !inputs.GlobalStop;
        string estopDetail;
        if (!inputs.AdsOnline)
            estopDetail = "E-stop status unavailable (ADS offline)";
        else if (!inputs.EstopOk)
            estopDetail = "Emergency stop circuit not OK or button pressed";
        else if (inputs.GlobalStop)
            estopDetail = "Global stop requested";
        else
            estopDetail = "E-stop circuit OK, no global stop";
        checks.Add(new InterlockCheck(InterlockCheckId.Estop, "E-Stop Circuit", estopOk, estopDetail));

        // 3. PlcQ
        bool plcQOk = inputs.AdsOnline && inputs.PlcQConnected;
        string plcQDetail;
        if (!inputs.AdsOnline)
            plcQDetail = "PLC-Q status unavailable (ADS offline)";
        else if (!inputs.PlcQConnected)
            plcQDetail = "PLC-Q queue bridge disconnected";
        else
            plcQDetail = "PLC-Q queue bridge connected";
        checks.Add(new InterlockCheck(InterlockCheckId.PlcQ, "PLC-Q Connection", plcQOk, plcQDetail));

        // 4. GantryFault
        bool gantryOk = inputs.AdsOnline && !inputs.GantryFault;
        string gantryDetail;
        if (!inputs.AdsOnline)
            gantryDetail = "Gantry status unavailable (ADS offline)";
        else if (inputs.GantryFault)
            gantryDetail = inputs.GantryBusy ? "Gantry fault active (cycle in progress)" : "Gantry fault active";
        else
            gantryDetail = inputs.GantryBusy ? "Gantry healthy (cycle in progress)" : "Gantry healthy and idle";
        checks.Add(new InterlockCheck(InterlockCheckId.GantryFault, "Gantry Status", gantryOk, gantryDetail));

        // 5. CameraFrame
        bool cameraOk = inputs.CameraConnected && inputs.CameraFrameFresh;
        string cameraDetail;
        if (!inputs.CameraConnected)
            cameraDetail = "No camera connected";
        else if (!inputs.CameraFrameFresh)
            cameraDetail = "Frame stale or missing";
        else
            cameraDetail = "Camera connected, frame fresh";
        checks.Add(new InterlockCheck(InterlockCheckId.CameraFrame, "Camera Frame", cameraOk, cameraDetail));

        // 6. SpeedSetpoint
        bool speedFinite = !double.IsNaN(inputs.PresetSpeedMmS) && !double.IsInfinity(inputs.PresetSpeedMmS);
        bool speedOk = speedFinite && inputs.PresetSpeedMmS >= LineSpeedPreset.MinMmS && inputs.PresetSpeedMmS <= LineSpeedPreset.MaxMmS;
        string speedDetail;
        if (!speedFinite)
            speedDetail = "Speed setpoint is not a valid number";
        else if (inputs.PresetSpeedMmS < LineSpeedPreset.MinMmS)
            speedDetail = $"Speed setpoint ({inputs.PresetSpeedMmS.ToString("F1", CultureInfo.InvariantCulture)} mm/s) is below minimum {LineSpeedPreset.MinMmS.ToString("F1", CultureInfo.InvariantCulture)} mm/s";
        else if (inputs.PresetSpeedMmS > LineSpeedPreset.MaxMmS)
            speedDetail = $"Speed setpoint ({inputs.PresetSpeedMmS.ToString("F1", CultureInfo.InvariantCulture)} mm/s) exceeds maximum {LineSpeedPreset.MaxMmS.ToString("F1", CultureInfo.InvariantCulture)} mm/s";
        else
            speedDetail = $"Speed setpoint valid ({inputs.PresetSpeedMmS.ToString("F1", CultureInfo.InvariantCulture)} mm/s)";
        checks.Add(new InterlockCheck(InterlockCheckId.SpeedSetpoint, "Speed Setpoint", speedOk, speedDetail));

        // 7. AutoContract
        bool autoContractOk = inputs.AutoContractCommissioned;
        string autoContractDetail = inputs.AutoContractCommissioned
            ? "TwinCAT AUTO mailbox is commissioned"
            : "TwinCAT AUTO mailbox is not commissioned";
        checks.Add(new InterlockCheck(InterlockCheckId.AutoContract, "TwinCAT AUTO Contract", autoContractOk, autoContractDetail));

        // 8. ScaraExcluded
        bool scaraOk = inputs.ScaraExcluded;
        string scaraDetail = inputs.ScaraExcluded
            ? "Station 02 excluded from Gantry-only AUTO"
            : "Station 02 not excluded from Gantry-only AUTO";
        checks.Add(new InterlockCheck(InterlockCheckId.ScaraExcluded, "SCARA Exclusion", scaraOk, scaraDetail));

        // TEST ONLY: mock mode simulates a healthy commissioned cell so the
        // interlock lifecycle can be demonstrated on a bench without camera,
        // E-stop feedback, PLC-Q or a commissioned AUTO mailbox.
        if (MockMode)
        {
            checks = checks.Select(c => new InterlockCheck(
                c.Id,
                c.Label,
                true,
                $"TEST ONLY SIMULATION - {c.Label}: no PLC/field signal exists; simulated OK, no PLC write.")).ToList();
        }

        if (!inputs.AdsOnline && !MockMode)
        {
            State = InterlockState.Offline;
            return new InterlockEvaluation(
                InterlockState.Offline,
                CanStart: false,
                CanStop: false,
                CanReset: false,
                PrimaryReason: "ADS 851 OFFLINE: no PLC snapshot; all line commands fail closed.",
                Checks: checks);
        }

        bool allOk = checks.All(c => c.Ok);

        if (State is InterlockState.Offline or InterlockState.NotReady or InterlockState.Ready)
        {
            State = allOk ? InterlockState.Ready : InterlockState.NotReady;
        }
        else if (State is InterlockState.StartRequested or InterlockState.RunningAcknowledged)
        {
            if (!allOk)
            {
                State = InterlockState.Fault;
            }
        }

        bool canStart = State == InterlockState.Ready && allOk;
        bool canStop = true;
        bool canReset = State is InterlockState.StopRequested or InterlockState.Fault;

        string primaryReason;
        if (!allOk)
        {
            primaryReason = checks.First(c => !c.Ok).Detail;
        }
        else if (State == InterlockState.Ready)
        {
            primaryReason = MockMode
                ? "READY TO REQUEST (TEST ONLY - simulated inputs, no PLC write)"
                : "READY TO REQUEST (commissioned contract required)";
        }
        else if (State == InterlockState.StartRequested)
        {
            primaryReason = "Start requested; awaiting PLC acknowledgment";
        }
        else if (State == InterlockState.RunningAcknowledged)
        {
            primaryReason = "Line running acknowledged";
        }
        else if (State == InterlockState.StopRequested)
        {
            primaryReason = "Line stop requested; reset required";
        }
        else // Fault
        {
            primaryReason = "Line fault latched; reset required";
        }

        return new InterlockEvaluation(State, canStart, canStop, canReset, primaryReason, checks);
    }

    public (bool accepted, string message) RequestStart(InterlockInputs inputs)
    {
        var eval = Evaluate(inputs);
        if (!eval.CanStart || State != InterlockState.Ready)
        {
            return (false, eval.PrimaryReason);
        }

        State = InterlockState.StartRequested;
        string message = MockMode
            ? "Tracking and conveyor start requested together (TEST ONLY - NO PLC WRITE)."
            : "Tracking and conveyor start requested together.";
        return (true, message);
    }

    public (bool accepted, string message) RequestStop(InterlockInputs inputs)
    {
        if (!inputs.AdsOnline && !MockMode)
        {
            State = InterlockState.Offline;
            return (false, "STOP blocked: no live ADS; use physical E-STOP in danger");
        }

        State = InterlockState.StopRequested;
        return (true, "Line stop requested (normal stop request, not a certified emergency stop).");
    }

    public (bool accepted, string message) RequestReset(InterlockInputs inputs)
    {
        if (!inputs.AdsOnline && !MockMode)
        {
            State = InterlockState.Offline;
            return (false, "RESET blocked: ADS 851 offline; line commands fail closed.");
        }

        if (State is not (InterlockState.StopRequested or InterlockState.Fault))
        {
            return (false, "RESET allowed only after a stop request or fault");
        }

        State = InterlockState.NotReady;
        return (true, "Interlock reset; line state set to NotReady.");
    }

    public void AcknowledgeStart()
    {
        if (State == InterlockState.StartRequested)
        {
            State = InterlockState.RunningAcknowledged;
        }
    }

    public void AcknowledgeStop()
    {
        if (State == InterlockState.StopRequested)
        {
            State = InterlockState.NotReady;
        }
    }
}
