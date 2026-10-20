using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlateVision50;

// Operator station overview: no hardware writes. All data comes from
// existing camera processing and read-only TwinCAT ADS polling.
public partial class MainWindow
{
    private DateTime _stationLastFrameUtc = DateTime.MinValue;
    private LineStatus? _lastSensorSnapshot;
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _stationSensorEvents = new();

    private void SensorHistory(string message)
    {
        _stationSensorEvents.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  •  " + message);
        while (_stationSensorEvents.Count > 80)
            _stationSensorEvents.RemoveAt(_stationSensorEvents.Count - 1);
    }

    private void RecordSensorEdges(LineStatus? now)
    {
        var old = _lastSensorSnapshot;
        _lastSensorSnapshot = now;
        if (now is null)
        {
            if (old is not null) SensorHistory("ADS 851 DISCONNECTED / PLC signals unavailable");
            return;
        }
        if (old is null)
        {
            SensorHistory("ADS 851 LIVE / signal monitoring started");
            return;
        }
        void Edge(string label,bool oldValue,bool newValue)
        {
            if (oldValue!=newValue) SensorHistory(label + ": " + (newValue ? "ON" : "OFF"));
        }
        Edge("E-STOP AUX DI0 (non safety-rated)",old.EstopOk,now.EstopOk);
        Edge("PLC SOFTWARE STOP",old.GlobalStop,now.GlobalStop);
        Edge("PLC-Q TCP",old.PlcQConnected,now.PlcQConnected);
        Edge("Gantry Busy",old.GantryBusy,now.GantryBusy);
        Edge("Gantry Alarm",old.GantryError,now.GantryError);
        Edge("MD200 RUN DO",old.BeltRun,now.BeltRun);
        bool wasServo = old.SymbolPresence.TryGetValue("MAIN.bServo_Ready",out var oldOk) && oldOk;
        bool isServo = now.SymbolPresence.TryGetValue("MAIN.bServo_Ready",out var newOk) && newOk;
        if (wasServo && isServo) Edge("Servo Ready",old.ServoReady,now.ServoReady);
    }


    private void StationFrameReceived()
    {
        _stationLastFrameUtc = DateTime.UtcNow;
        UpdateStationOverviewVision();
    }

    private void InitStationOverview()
    {
        // The existing ObservableCollection updates the event log when a product
        // crosses the image trigger; no duplicated tracking process.
        OverviewGantryLog.ItemsSource = _auditRows;
        OverviewSensorLog.ItemsSource = _stationSensorEvents;
        UpdateStationOverviewVision();
        OverviewGlobalAuthority.Text = "Global START/STOP not commissioned; use the physical E-Stop circuit";
    }

    private void UpdateStationOverviewVision()
    {
        if (_closing) return;
        bool sourceFresh = Preview.Source != null && HasActiveSource
            && DateTime.UtcNow - _stationLastFrameUtc <= TimeSpan.FromSeconds(3);
        bool sourceVideo = _video != null;
        string source = sourceVideo ? "VIDEO TEST" :
            (_directHik?.IsOpen == true || _sessions.Count > 0) ? "CAMERA 1" : "DISCONNECTED";
        string freshness = sourceFresh ? "FRAME LIVE" : "NO FRAME / STALE";
        OverviewCam1Quality.Text = source + " • " + freshness +
            (_trackingEnabled ? " • TRACKING" : " • DETECT OFF");
        OverviewCam1Empty.Visibility = sourceFresh ? Visibility.Collapsed : Visibility.Visible;

        // Camera coordinates belong to the most recently evaluated detection,
        // never to an encoder-predicted robot target or a fake SCARA source.
        bool validPose = sourceFresh && _trackingEnabled && _last.Found &&
            double.IsFinite(_lastBelt.Xmm) && double.IsFinite(_lastBelt.Ymm) &&
            double.IsFinite(_last.AngleDeg);
        string track = _overlayTrackIds.Count > 0
            ? string.Join(", ", _overlayTrackIds.Values.Distinct().OrderBy(x => x).Take(6))
            : "--";
        OverviewCam1Track.Text = validPose ? "Track ID: " + track : "Track ID: --";
        OverviewCam1Pose.Text = validPose
            ? $"X {_lastBelt.Xmm:F1}  Y {_lastBelt.Ymm:F1}  θ {_last.AngleDeg:F1}°"
            : "X --  Y --  θ --";
        StationCam1Status.Text = source + " | " + freshness + (_trackingEnabled ? " | DETECT ON" : " | DETECT OFF")
            + (sourceVideo ? " | No synchronized physical encoder" : "");
        StationCam1Coords.Text = validPose
            ? $"Track {track} | X {_lastBelt.Xmm:F2} mm | Y {_lastBelt.Ymm:F2} mm | θ {_last.AngleDeg:F2}°"
            : "Track -- | X -- / Y -- / θ --";
        if (_events.Count > 0)
            OverviewRecentEvent.Text = "CAMERA 1 / " + _events[0];
    }

    private void UpdateStationOverviewIoIndicators(LineStatus? status)
    {
        // ADS port 851 diagnostic snapshot. These are NOT safety-rated I/O indicators.
        void Pin(System.Windows.Controls.TextBlock element, string label, bool? value, string high="ON",string low="OFF",bool alarmOn=false,bool criticalOff=false)
        {
            element.Text = label + "  |  " + (value == null ? "UNAVAILABLE" : value.Value ? high : low);
            bool fault = value.HasValue && ((alarmOn && value.Value) || (criticalOff && !value.Value));
            element.Foreground = DashBrush(value == null ? "#E4C47A" : fault ? "#FF8A85" : value.Value ? "#7FDBC4" : "#B9CAD6");
        }
        if (status is null)
        {
            Pin(OverviewIoEstop,"ST-1228 DI0 AUX",null);
            Pin(OverviewIoServo,"SERVO READY",null);
            Pin(OverviewIoPlcQ,"PLC-Q LINK",null);
            Pin(OverviewIoGantryBusy,"GANTRY BUSY",null);
            Pin(OverviewIoGantryAlarm,"GANTRY ALARM",null);
            Pin(OverviewIoVision,"VISION BRIDGE",null);
            Pin(OverviewIoBeltDO,"ST-2318 DO0",null);
            Pin(OverviewIoMd200Cmd,"MD200 START REQ",null);
            Pin(OverviewIoEncoder,"ST-5114 ENCODER",null);
            Pin(OverviewIoAo,"ST-4424 AO1",null);
            return;
        }
        bool? Optional(string symbol, bool value) =>
            status.SymbolPresence.TryGetValue(symbol,out var ok) && ok ? value : null;
        bool? Encoder = status.SymbolPresence.TryGetValue("MAIN.rConveyorSpeed",out var okSpeed)
            && okSpeed ? Math.Abs(status.BeltSpeed)>0.01 : null;
        Pin(OverviewIoEstop,"ST-1228 DI0 AUX",status.EstopOk,"ON","OFF",criticalOff:true);
        Pin(OverviewIoServo,"SERVO READY",Optional("MAIN.bServo_Ready",status.ServoReady));
        Pin(OverviewIoPlcQ,"PLC-Q LINK",Optional("MAIN.bPlcQConnected",status.PlcQConnected),"CONNECTED","DISCONNECTED",criticalOff:true);
        Pin(OverviewIoGantryBusy,"GANTRY BUSY",Optional("MAIN.bGlueCycleBusy",status.GantryBusy));
        Pin(OverviewIoGantryAlarm,"GANTRY ALARM",Optional("MAIN.bGlueCycleError",status.GantryError),alarmOn:true);
        Pin(OverviewIoVision,"VISION BRIDGE",Optional("MAIN.bVisionFeedVerified",status.VisionBridgeVerified));
        Pin(OverviewIoBeltDO,"ST-2318 DO0",status.BeltRun);
        Pin(OverviewIoMd200Cmd,"MD200 START REQ",status.BeltRequest);
        Pin(OverviewIoEncoder,"ST-5114 ENCODER",Encoder,"MOVING","STATIONARY");
        OverviewIoAo.Text=$"ST-4424 AO1  |  {status.AoRaw} RAW";
        OverviewIoAo.Foreground=DashBrush("#B9CAD6");
    }

    private void UpdateStationOverviewPlc(LineStatus? status)
    {
        RecordSensorEdges(status);
        UpdateStationOverviewIoIndicators(status);
        if (status is null)
        {
            OverviewSensors.Text = "ADS OFFLINE / input feedback unavailable; machine safety is unverified.";
            OverviewGantrySensors.Text = "PLC-Q / Gantry / Encoder: OFFLINE";
            OverviewGlobalAuthority.Text = "ADS OFFLINE · GLOBAL START/STOP NOT WIRED TO PLC";
            UpdateStationOverviewVision();
            return;
        }
        string BoolState(bool v) => v ? "ON" : "OFF";
        string Quality(string tag) => status.SymbolPresence.TryGetValue(tag, out var q) && q ? "LIVE" : "READ_FAIL";
        string servo = Quality("MAIN.bServo_Ready") == "READ_FAIL" ? "READ_FAIL" : BoolState(status.ServoReady);
        OverviewSensors.Text =
            $"E-STOP AUX DI0: {BoolState(status.EstopOk)}  |  TwinCAT STOP REQUEST: {BoolState(status.GlobalStop)}  |  " +
            $"SERVO READY: {servo}  |  MD200 RUN DO: {BoolState(status.BeltRun)}  |  ENCODER: {status.BeltSpeed:F2} mm/s";
        OverviewGantrySensors.Text =
            $"PLC-Q TCP: {(status.PlcQConnected ? "CONNECTED" : "DISCONNECTED")}  |  " +
            $"VISION BRIDGE: {BoolState(status.VisionBridgeVerified)}  |  GANTRY BUSY: {BoolState(status.GantryBusy)}" +
            $"  |  ALARM: {BoolState(status.GantryError)}";
        OverviewGlobalAuthority.Text = status.EstopOk && !status.GlobalStop
            ? "ADS LIVE · GLOBAL START/STOP NOT COMMISSIONED; PHYSICAL E-STOP REQUIRED"
            : "PLC STOP REQUESTED / E-STOP AUX NOT OK · DO NOT START EQUIPMENT";
        UpdateStationOverviewVision();
    }

    private void DashGlobalCommand_Click(object sender, RoutedEventArgs e)
    {
        var action = (sender as Button)?.Tag?.ToString() ?? "UNKNOWN";
        string details = action switch
        {
            "START" => "GLOBAL START is not connected to TwinCAT. A commissioned Cmd/Ack handshake and validated interlocks are required for Gantry, SCARA, conveyor and the physical emergency stop. NO PLC COMMAND WAS SENT.",
            "STOP" => "GLOBAL STOP requires a validated PLC sequence, robot cancellation and conveyor stop acknowledgement. No commissioned global stop tag exists. NO PLC COMMAND WAS SENT.",
            "ESTOP" => "THIS IS NOT AN EMERGENCY-STOP CONTROL. In danger, use the PHYSICAL E-STOP button on the machine. This UI only monitors auxiliary DI0 feedback and cannot remove hazardous energy.",
            _ => "No command."
        };
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        OverviewRecentEvent.Text = $"{stamp} · HMI REQUEST {action}: NOT SENT TO PLC";
        MessageBox.Show(details, "DATN / Global Command Information",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DashOpenGantry_Click(object sender, RoutedEventArgs e) =>
        MainNavigation.SelectedItem = GantryDetailTab;
    private void DashOpenScara_Click(object sender, RoutedEventArgs e) =>
        MainNavigation.SelectedItem = ScaraDetailTab;
    private void DashOpenConveyor_Click(object sender, RoutedEventArgs e) =>
        MainNavigation.SelectedItem = ConveyorDetailTab;

    private void StationVisionStart_Click(object sender, RoutedEventArgs e)
    {
        // Existing image-only handler disarms guarded ADS benchmark writes.
        StartTracking_Click(sender, e);
        UpdateStationOverviewVision();
    }

    private void StationVisionStop_Click(object sender, RoutedEventArgs e)
    {
        StopTracking_Click(sender, e);
        UpdateStationOverviewVision();
    }
}
