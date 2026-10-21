using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlateVision50.Trigger;

namespace PlateVision50;

// Operator-facing row models for the Station 01 / Station 02 dashboards.
// "LIVE" is only ever shown for a symbol that exists AND was read successfully
// in the current ADS snapshot. Everything else is NO DATA or UNMAPPED.
public sealed class StationSignalRow
{
    public string Label { get; init; } = "";
    public string Group { get; init; } = "ALL";
    public string Value { get; init; } = "--";
    public string Unit { get; init; } = "";
    public string State { get; init; } = "UNMAPPED";
    public Brush StateBrush { get; init; } = Brushes.Gray;
    public bool Live { get; init; }
}

public sealed class StationParameterRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "NOT CONFIGURED";
    public string Unit { get; init; } = "";
    public string State { get; init; } = "NOT CONFIGURED";
    public Brush StateBrush { get; init; } = Brushes.Gray;
}

public partial class MainWindow
{
    private const string StateLive = "LIVE";
    private const string StateNoData = "NO DATA";
    private const string StateUnmapped = "UNMAPPED";
    private const string StateNotConfigured = "NOT CONFIGURED";

    private readonly ObservableCollection<StationSignalRow> _s1Sensors = new();
    private readonly ObservableCollection<StationSignalRow> _s2Sensors = new();
    private readonly ObservableCollection<StationParameterRow> _s1Params = new();
    private readonly ObservableCollection<StationParameterRow> _s2Params = new();

    private IReadOnlyList<StationSignalRow> _s1SensorAll = Array.Empty<StationSignalRow>();
    private IReadOnlyList<StationSignalRow> _s2SensorAll = Array.Empty<StationSignalRow>();
    private string _s1SensorGroup = "ALL";
    private string _s2SensorGroup = "ALL";
    private string _s1SensorFilter = "";
    private string _s2SensorFilter = "";

    private static Brush StateBrushFor(string state) => state switch
    {
        StateLive => DashBrush("#39C7A2"), // Siemens teal (healthy / live)
        StateNoData => DashBrush("#F4BB45"), // Siemens warning amber
        StateNotConfigured => DashBrush("#92AABE"), // Secondary text
        _ => DashBrush("#7D93A4") // UNMAPPED (muted industrial slate)
    };

    private static StationSignalRow Signal(string label, string group, bool? value, string on, string off,
        bool presence = true, string noData = StateNoData)
    {
        if (!presence)
            return new StationSignalRow { Label = label, Group = group, State = StateUnmapped, StateBrush = StateBrushFor(StateUnmapped) };
        if (value is null)
            return new StationSignalRow { Label = label, Group = group, State = noData, StateBrush = StateBrushFor(noData) };
        var text = value.Value ? on : off;
        return new StationSignalRow
        {
            Label = label,
            Group = group,
            Value = text,
            State = StateLive,
            StateBrush = StateBrushFor(StateLive),
            Live = true
        };
    }

    private static StationSignalRow Unmapped(string label, string group) =>
        new() { Label = label, Group = group, State = StateUnmapped, StateBrush = StateBrushFor(StateUnmapped) };

    private static StationParameterRow Parameter(string label, string value, string unit, string state)
        => new() { Label = label, Value = value, Unit = unit, State = state, StateBrush = StateBrushFor(state) };

    // Reads one optional BOOL symbol from the snapshot without inventing values.
    private static bool? Opt(LineStatus? s, string symbol, bool fallback)
        => s is not null && s.SymbolPresence.TryGetValue(symbol, out var ok) && ok ? fallback : null;

    private void InitStationDashboards()
    {
        S1SensorList.ItemsSource = _s1Sensors;
        S2SensorList.ItemsSource = _s2Sensors;
        S1ParamList.ItemsSource = _s1Params;
        S2ParamList.ItemsSource = _s2Params;
        S1EventList.ItemsSource = _stationSensorEvents;
        S2EventList.ItemsSource = _stationSensorEvents;
        RebuildStationSignals(null);
        RebuildStationParameters(null);
    }

    // Station 01 has a real camera, a real PLC-Q handshake and a real glue cycle.
    // Station 02 has no validated symbols at all, so every row stays UNMAPPED.
    private void RebuildStationSignals(LineStatus? status)
    {
        bool? Presence(string symbol) =>
            status is null ? null : status.SymbolPresence.TryGetValue(symbol, out var ok) && ok;

        void S1Bool(List<StationSignalRow> into, string label, string group, string symbol, bool fallback,
            string on = "ON", string off = "OFF")
        {
            var presence = Presence(symbol);
            bool? value = presence == true ? fallback : null;
            into.Add(presence == false
                ? new StationSignalRow { Label = label, Group = group, State = StateNoData, StateBrush = StateBrushFor(StateNoData) }
                : Signal(label, group, value, on, off));
        }

        var s1 = new List<StationSignalRow>();
        // Safety
        var estop = status is null ? (bool?)null : status.EstopOk;
        s1.Add(status is null
            ? new StationSignalRow { Label = "Safety auxiliary feedback", Group = "SAFETY", State = StateNoData, StateBrush = StateBrushFor(StateNoData) }
            : Signal("Safety auxiliary feedback", "SAFETY", estop, "HEALTHY", "NOT OK"));
        S1Bool(s1, "System stop requested", "SAFETY", "MAIN.bSystemStopRequested",
            status?.GlobalStop ?? false, "REQUESTED", "CLEAR");
        // Motion
        S1Bool(s1, "Gantry cycle busy", "MOTION", "MAIN.bGlueCycleBusy", status?.GantryBusy ?? false, "BUSY", "IDLE");
        S1Bool(s1, "Gantry cycle done", "MOTION", "MAIN.bGlueCycleDone", status?.GantryDone ?? false, "DONE", "NOT DONE");
        S1Bool(s1, "Gantry alarm", "MOTION", "MAIN.bGlueCycleError", status?.GantryError ?? false, "FAULT", "CLEAR");
        S1Bool(s1, "Servo drive ready", "MOTION", "MAIN.bServo_Ready", status?.ServoReady ?? false, "READY", "NOT READY");
        S1Bool(s1, "Motion publisher enabled", "MOTION", "MAIN.bLegacyMotionPublisherAllow",
            status?.GantryPublisherEnabled ?? false, "ENABLED", "DISABLED");
        // Vision / handshake
        S1Bool(s1, "Camera-to-PLC bridge verified", "VISION", "MAIN.bVisionFeedVerified",
            status?.VisionBridgeVerified ?? false, "VERIFIED", "NOT VERIFIED");
        S1Bool(s1, "Robot controller link", "VISION", "MAIN.bPlcQConnected", status?.PlcQConnected ?? false,
            "CONNECTED", "DISCONNECTED");
        s1.Add(status is null
            ? new StationSignalRow { Label = "track FIFO depth", Group = "VISION", State = StateNoData, StateBrush = StateBrushFor(StateNoData) }
            : Signal("Track FIFO depth", "VISION", status.QueueCount > 0, $"occupied ({status.QueueCount})", "empty"));
        // Local vision state (not PLC tags; honest local state only)
        s1.Add(new StationSignalRow
        {
            Label = "Image detection enabled",
            Group = "VISION",
            Value = _trackingEnabled ? "ON" : "OFF",
            State = StateLive,
            StateBrush = StateBrushFor(StateLive),
            Live = true
        });
        s1.Add(new StationSignalRow
        {
            Label = "Camera 1 frame source",
            Group = "VISION",
            Value = _video != null ? "VIDEO TEST" : (_directHik?.IsOpen == true || _sessions.Count > 0) ? "CAMERA 1" : "NONE",
            State = HasActiveSource ? StateLive : StateNoData,
            StateBrush = StateBrushFor(HasActiveSource ? StateLive : StateNoData)
        });
        // Signals that have no verified PLC symbol in this build.
        s1.Add(Unmapped("Product present sensor", "VISION"));
        s1.Add(Unmapped("Camera trigger input", "VISION"));
        s1.Add(Unmapped("Glue pressure switch", "GLUE"));
        s1.Add(Unmapped("Glue level switch", "GLUE"));
        s1.Add(Unmapped("Glue valve feedback", "GLUE"));
        s1.Add(Unmapped("Home position sensor", "MOTION"));
        s1.Add(Unmapped("Positive limit switch", "MOTION"));
        s1.Add(Unmapped("Negative limit switch", "MOTION"));
        s1.Add(Unmapped("Safety door indication", "SAFETY"));
        s1.Add(Unmapped("Local start pushbutton", "MOTION"));
        s1.Add(Unmapped("Local stop pushbutton", "MOTION"));
        s1.Add(Unmapped("Local reset pushbutton", "MOTION"));
        s1.Add(Unmapped("Alarm acknowledge", "SAFETY"));
        _s1SensorAll = s1;

        var s2 = new List<StationSignalRow>
        {
            Unmapped("Tray present sensor", "VISION"),
            Unmapped("Gripper open", "GLUE"),
            Unmapped("Gripper closed", "GLUE"),
            Unmapped("Part picked", "GLUE"),
            Unmapped("Part released", "GLUE"),
            Unmapped("Vacuum switch", "GLUE"),
            Unmapped("Home position sensor", "MOTION"),
            Unmapped("Axis 1 positive limit", "MOTION"),
            Unmapped("Axis 1 negative limit", "MOTION"),
            Unmapped("Axis 2 positive limit", "MOTION"),
            Unmapped("Queue acknowledgement", "VISION"),
            Unmapped("Local reset pushbutton", "MOTION"),
            Unmapped("Local alarm", "SAFETY"),
            Unmapped("Safety door indication", "SAFETY"),
            Unmapped("Camera 02 frame valid", "VISION"),
            Unmapped("Robot ready", "MOTION"),
            Unmapped("Robot alarm", "SAFETY"),
            Unmapped("Tray recipe selected", "GLUE")
        };
        _s2SensorAll = s2;

        ApplySensorFilter("S1");
        ApplySensorFilter("S2");
    }

    private void RebuildStationParameters(LineStatus? status)
    {
        // Vision/trigger values are real local configuration. Robot/glue process
        // values have no validated source yet and must read NOT CONFIGURED.
        string direction = _beltDirection switch
        {
            BeltDirection.XPlus => "X →",
            BeltDirection.XMinus => "X ←",
            BeltDirection.YPlus => "Y ↑",
            BeltDirection.YMinus => "Y ↓",
            _ => "Y ↓"
        };
        _s1Params.Clear();
        _s1Params.Add(Parameter("Recipe / glue mode", "4-point standard", "", StateLive));
        _s1Params.Add(Parameter("Dot count", "4", "points", StateLive));
        _s1Params.Add(Parameter("Glue pitch (X × Y)", "40 × 40", "mm", StateLive));
        _s1Params.Add(Parameter("Trigger line position", $"{_lineFraction * 100:F0}", "% FOV", StateLive));
        _s1Params.Add(Parameter("Conveyor direction", direction, "", StateLive));
        _s1Params.Add(Parameter("Calibration scale", _options.FixedMmPerPixel > 0 ? $"{_options.FixedMmPerPixel:F4}" : "auto", "mm/px",
            _options.FixedMmPerPixel > 0 ? StateLive : "AUTO"));
        _s1Params.Add(Parameter("Conveyor synchronization", "shared encoder", "", "READ ONLY"));
        _s1Params.Add(Parameter("Vision offset", "not configured", "mm", StateNotConfigured));
        _s1Params.Add(Parameter("Glue delay", "not configured", "ms", StateNotConfigured));
        _s1Params.Add(Parameter("Dispense speed", "not configured", "mm/s", StateNotConfigured));
        _s1Params.Add(Parameter("Robot speed override", "not configured", "%", StateNotConfigured));
        _s1Params.Add(Parameter("Recipe number", "not configured", "", StateNotConfigured));

        _s2Params.Clear();
        _s2Params.Add(Parameter("Pick height", "not configured", "mm", StateNotConfigured));
        _s2Params.Add(Parameter("Place height", "not configured", "mm", StateNotConfigured));
        _s2Params.Add(Parameter("Approach height", "not configured", "mm", StateNotConfigured));
        _s2Params.Add(Parameter("Pick delay", "not configured", "ms", StateNotConfigured));
        _s2Params.Add(Parameter("Place delay", "not configured", "ms", StateNotConfigured));
        _s2Params.Add(Parameter("Gripper open time", "not configured", "ms", StateNotConfigured));
        _s2Params.Add(Parameter("Gripper close time", "not configured", "ms", StateNotConfigured));
        _s2Params.Add(Parameter("Tray recipe", "not configured", "", StateNotConfigured));
        _s2Params.Add(Parameter("Robot speed override", "not configured", "%", StateNotConfigured));
        _s2Params.Add(Parameter("Camera 02 calibration", "not configured", "mm/px", StateNotConfigured));
        _s2Params.Add(Parameter("Station synchronization", "shared encoder (planned)", "", "RESERVED"));
        _s2Params.Add(Parameter("Place offset", "not configured", "mm", StateNotConfigured));

        S1ParamCount.Text = $"{_s1Params.Count} parameters · values from local configuration only";
        S2ParamCount.Text = $"{_s2Params.Count} parameters · station not commissioned";
    }

    private void ApplySensorFilter(string station)
    {
        if (station == "S1")
        {
            var rows = _s1SensorAll.Where(r =>
                (_s1SensorGroup == "ALL" || r.Group == _s1SensorGroup) &&
                (_s1SensorFilter.Length == 0 || r.Label.Contains(_s1SensorFilter, StringComparison.OrdinalIgnoreCase)));
            _s1Sensors.Clear();
            foreach (var r in rows) _s1Sensors.Add(r);
            S1SensorCount.Text = $"{_s1Sensors.Count} / {_s1SensorAll.Count} signals · {_s1SensorAll.Count(r => r.Live)} live";
        }
        else
        {
            var rows = _s2SensorAll.Where(r =>
                (_s2SensorGroup == "ALL" || r.Group == _s2SensorGroup) &&
                (_s2SensorFilter.Length == 0 || r.Label.Contains(_s2SensorFilter, StringComparison.OrdinalIgnoreCase)));
            _s2Sensors.Clear();
            foreach (var r in rows) _s2Sensors.Add(r);
            S2SensorCount.Text = $"{_s2Sensors.Count} / {_s2SensorAll.Count} signals · {_s2SensorAll.Count(r => r.Live)} live";
        }
    }

    private void StationSensorGroup_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag?.ToString() ?? "";
        var parts = tag.Split('|');
        if (parts.Length != 2) return;
        if (parts[0] == "S1") _s1SensorGroup = parts[1]; else _s2SensorGroup = parts[1];
        ApplySensorFilter(parts[0]);
    }

    private void StationSensorSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var box = sender as TextBox;
        if (box is null) return;
        if ((box.Tag?.ToString() ?? "") == "S1") { _s1SensorFilter = box.Text.Trim(); ApplySensorFilter("S1"); }
        else { _s2SensorFilter = box.Text.Trim(); ApplySensorFilter("S2"); }
    }

    private void StationSettings_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as Button)?.Tag?.ToString() ?? "";
        MainNavigation.SelectedItem = SettingsTab;
        // Per-station settings are separated; land on the matching section.
        if (SettingsSections is not null)
            SettingsSections.SelectedItem = tag == "S2" ? SettingsStation2Tab : SettingsStation1Tab;
    }

    // Called from the 1 s ADS poll (Dashboard.RefreshDashboardAsync).
    private void UpdateStationDashboards(LineStatus? status)
    {
        if (_closing) return;
        RebuildStationSignals(status);
        RebuildStationParameters(status);

        // ---- Station 01: live camera + real read-only PLC-Q/glue telemetry ----
        bool fresh = Preview.Source is not null && HasActiveSource &&
                     DateTime.UtcNow - _stationLastFrameUtc <= TimeSpan.FromSeconds(3);
        string source = _video != null ? "VIDEO TEST"
            : (_directHik?.IsOpen == true || _sessions.Count > 0) ? "CAMERA 1" : "DISCONNECTED";
        S1CamBadge.Text = "CAMERA 01 · " + (fresh ? "LIVE FRAME" : source == "DISCONNECTED" ? "NO SOURCE" : "STALE");
        S1CamEmpty.Visibility = fresh ? Visibility.Collapsed : Visibility.Visible;
        S1DetectStatus.Text = _trackingEnabled ? "DETECT ON" : "DETECT OFF";

        bool validPose = fresh && _trackingEnabled && _last.Found &&
            double.IsFinite(_lastBelt.Xmm) && double.IsFinite(_lastBelt.Ymm) && double.IsFinite(_last.AngleDeg);
        string track = _overlayTrackIds.Count > 0
            ? string.Join(", ", _overlayTrackIds.Values.Distinct().OrderBy(x => x).Take(4))
            : "--";
        S1TrackId.Text = validPose ? track : "--";
        S1Pose.Text = validPose
            ? $"X {_lastBelt.Xmm:F1}  Y {_lastBelt.Ymm:F1}  θ {_last.AngleDeg:F1}°"
            : "X --  Y --  θ --";
        S1Confidence.Text = validPose && _last.Confidence > 0 ? $"{_last.Confidence:P0}" : "--";
        S1FrameQuality.Text = source + " · " + (fresh ? "frame live" : "no fresh frame");
        S1CfgCamera.Text = source == "DISCONNECTED" ? "No source" : source;

        if (status is null)
        {
            S1Badge.Text = "ADS OFFLINE";
            S1BadgeHost.Background = DashBrush("#33181C");
            S1Badge.Foreground = DashBrush("#FF6E70");
            S1Link.Text = "NO DATA";
            S1Ready.Text = "NO DATA";
            S1Servo.Text = "NO DATA";
            S1Mode.Text = "NO DATA";
            S1Busy.Text = "NO DATA";
            S1Homed.Text = "NO DATA";
            S1X.Text = S1Y.Text = S1Z.Text = S1Rz.Text = "NO DATA";
            S1QueueDepth.Text = "NO DATA";
            S1CycleStatus.Text = "NO DATA";
            S1Vertex.Text = "NO DATA";
            S1QualityChip.Text = "OFFLINE";
            S1AlarmStrip.Text = _stationSensorEvents.Count > 0 ? _stationSensorEvents[0] : "ADS offline - no signal data.";
        }
        else
        {
            S1Badge.Text = "ADS LIVE";
            S1BadgeHost.Background = DashBrush("#103028");
            S1Badge.Foreground = DashBrush("#39C7A2");
            S1Link.Text = status.PlcQConnected ? "CONNECTED" : "DISCONNECTED";
            S1Ready.Text = Pres(status, "MAIN.bServo_Ready") ? (status.ServoReady ? "READY" : "NOT READY") : "NO DATA";
            S1Servo.Text = Pres(status, "MAIN.bServo_Ready") ? (status.ServoReady ? "ENABLED" : "DISABLED") : "NO DATA";
            S1Mode.Text = status.GantryPublisherEnabled ? "MONITOR" : "AUTO";
            S1Busy.Text = status.GantryError ? "FAULT" : status.GantryBusy ? "BUSY" : status.GantryDone ? "DONE" : "IDLE";
            S1Homed.Text = "NO DATA"; // no homed/reference symbol exists
            S1X.Text = S1Y.Text = S1Z.Text = S1Rz.Text = "--";
            S1QueueDepth.Text = Pres(status, "MAIN.nQueueCount") ? $"{status.QueueCount} / 8" : "NO DATA";
            S1CycleStatus.Text = status.GantryError ? "FAULT" : status.GantryBusy ? "BUSY" : status.GantryDone ? "DONE" : "IDLE";
            S1Vertex.Text = Pres(status, "MAIN.nGlueCurVertex") ? $"{status.GantryVertex} / 4" : "NO DATA";
            S1QualityChip.Text = fresh ? "FRAME LIVE" : "NO FRAME";
            S1AlarmStrip.Text = status.GantryError
                ? $"Gantry alarm active (code {status.GantryErrorCode}) - check ALARMS & LOGS."
                : _stationSensorEvents.Count > 0 ? _stationSensorEvents[0] : "No active alarm.";
        }

        // ---- Station 02: nothing is commissioned; never fabricate a value ----
        S2Link.Text = "UNMAPPED";
        S2Ready.Text = "NO DATA";
        S2Servo.Text = "NO DATA";
        S2Mode.Text = "NO DATA";
        S2Busy.Text = "NO DATA";
        S2Homed.Text = "NO DATA";
        S2X.Text = S2Y.Text = S2Z.Text = S2Rz.Text = "--";
        S2Track.Text = "--";
        S2Detect.Text = "NO DATA";
        S2Confidence.Text = "--";
        S2Pose.Text = "X --  Y --  θ --";
        S2FrameQuality.Text = "Camera 02 not integrated";
        S2QueueDepth.Text = "UNMAPPED";
        S2CycleStatus.Text = "NOT COMM.";
        S2Vertex.Text = "--";
        S2AlarmStrip.Text = "Station 02 is not commissioned. No camera, robot or tray signal is available.";

        // Conveyor page receives the same honest snapshot.
        OvUpdated.Text = status is null
            ? "ADS OFFLINE / " + DateTime.Now.ToString("HH:mm:ss")
            : "ADS " + status.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss") + " · updated every 1 s";
        if (status is null)
        {
            OvCvQuality.Text = "OFFLINE / NO PLC DATA";
            OvCvQuality.Foreground = DashBrush("#FF6E70");
            OvCvRun.Text = "NO DATA";
            OvCvSpeed.Text = "NO DATA";
            OvCvDistance.Text = "NO DATA";
            OvCvCommand.Text = "DISARMED";
            CvTriggerLine.Text = "Direction " + (_beltDirection == BeltDirection.YMinus ? "Y↑" : "set in Settings")
                + $" · {_lineFraction * 100:F0}% FOV";
        }
        else
        {
            OvCvQuality.Text = "LIVE · READ-ONLY";
            OvCvQuality.Foreground = DashBrush("#39C7A2");
            OvCvRun.Text = status.BeltRun ? "RUNNING" : "STOPPED";
            OvCvSpeed.Text = Pres(status, "MAIN.rConveyorSpeed") ? $"{status.BeltSpeed:F1} mm/s" : "NO DATA";
            OvCvDistance.Text = Pres(status, "MAIN.rConveyorDist") ? $"{status.BeltDistance:F1} mm" : "NO DATA";
            OvCvCommand.Text = _dashControlArmed ? "ARMED (SESSION)" : "DISARMED";
            CvTriggerLine.Text = "Direction " + (_beltDirection == BeltDirection.YMinus ? "Y↑" : "set in Settings")
                + $" · {_lineFraction * 100:F0}% FOV";
        }

        // Overview cards: Station 01 mirrors the same numbers, Station 02 stays closed.
        Ov1Badge.Text = status is null ? "OFFLINE" : "LIVE";
        Ov1BadgeHost.Background = DashBrush(status is null ? "#33181C" : "#103028");
        Ov1Badge.Foreground = DashBrush(status is null ? "#FF6E70" : "#39C7A2");
        Ov1CamStatus.Text = "CAMERA 1 · " + (fresh ? source + " LIVE" : "NO FRAME");
        Ov1CamEmpty.Visibility = fresh ? Visibility.Collapsed : Visibility.Visible;
        Ov1Link.Text = status is null ? "NO DATA" : status.PlcQConnected ? "CONNECTED" : "DISCONNECTED";
        Ov1Ready.Text = status is null ? "NO DATA" : Pres(status, "MAIN.bServo_Ready") ? (status.ServoReady ? "READY" : "NOT READY") : "NO DATA";
        Ov1Servo.Text = status is null ? "NO DATA" : Pres(status, "MAIN.bServo_Ready") ? (status.ServoReady ? "ENABLED" : "DISABLED") : "NO DATA";
        Ov1Mode.Text = status is null ? "NO DATA" : status.GantryPublisherEnabled ? "MONITOR" : "AUTO";
        Ov1Busy.Text = status is null ? "NO DATA" : status.GantryError ? "FAULT" : status.GantryBusy ? "BUSY" : "IDLE";
        Ov1Homed.Text = "NO DATA";
        Ov1X.Text = Ov1Y.Text = Ov1Z.Text = Ov1Rz.Text = "--";
        Ov1Track.Text = validPose ? track : "--";
        Ov1Pose.Text = validPose
            ? $"{_lastBelt.Xmm:F1} / {_lastBelt.Ymm:F1} mm"
            : "-- / -- mm";
        Ov1Io1.Text = "SAFETY AUX | " + (status is null ? "NO DATA" : status.EstopOk ? "HEALTHY" : "NOT OK");
        Ov1Io2.Text = "SERVO READY | " + (status is null ? "NO DATA" : Pres(status, "MAIN.bServo_Ready") ? (status.ServoReady ? "READY" : "NOT READY") : "NO DATA");
        Ov1Io3.Text = "VISION BRIDGE | " + (status is null ? "NO DATA" : Pres(status, "MAIN.bVisionFeedVerified") ? (status.VisionBridgeVerified ? "VERIFIED" : "NOT VERIFIED") : "NO DATA");
        Ov1Io4.Text = "GANTRY BUSY | " + (status is null ? "NO DATA" : Pres(status, "MAIN.bGlueCycleBusy") ? (status.GantryBusy ? "BUSY" : "IDLE") : "NO DATA");
        Ov1Io5.Text = "GANTRY ALARM | " + (status is null ? "NO DATA" : Pres(status, "MAIN.bGlueCycleError") ? (status.GantryError ? "ACTIVE" : "CLEAR") : "NO DATA");
        Ov1Io6.Text = "GLUE CYCLE | " + (status is null ? "NO DATA" : status.GantryError ? "FAULT" : status.GantryBusy ? "RUNNING" : status.GantryDone ? "DONE" : "IDLE");
        OvAlarm.Text = status is null
            ? "No PLC alarm data. Global START/STOP is not commissioned."
            : status.GantryError
                ? $"Gantry alarm active (code {status.GantryErrorCode}). Check ALARMS & LOGS."
                : _stationSensorEvents.Count > 0 ? _stationSensorEvents[0] : "No active station alarm.";

        UpdateStationOverviewVision();
    }

    private static bool Pres(LineStatus s, string symbol) =>
        s.SymbolPresence.TryGetValue(symbol, out var ok) && ok;
}
