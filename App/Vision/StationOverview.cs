using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using PlateVision50.Vision;
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
        Edge("PLC-Q MC Error",old.PlcQError,now.PlcQError);
        Edge("Gantry Stop Confirmed",old.StopCommandConfirmed,now.StopCommandConfirmed);
        Edge("MD200 RUN DO",old.BeltRun,now.BeltRun);
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
        OverviewGlobalAuthority.Text = "AUTO START: fail-closed until the TwinCAT AUTO contract is commissioned; physical E-Stop remains independent";
    }

    private void UpdateStationOverviewVision()
    {
        if (_closing) return;
        // Camera detection is PC-only and can be started from LINE OVERVIEW.
        OvVisionStartButton.IsEnabled = HasActiveSource && !_trackingEnabled;
        OvVisionStopButton.IsEnabled = _trackingEnabled;
        bool sourceFresh = Preview.Source != null && HasActiveSource
            && DateTime.UtcNow - _stationLastFrameUtc <= TimeSpan.FromSeconds(3);
        bool sourceVideo = _video != null;
        string source = sourceVideo ? "VIDEO TEST" : _cam1.IsOpen ? "CAM 1" : "DISCONNECTED";
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
        if (_events.Count > 0)
            OverviewRecentEvent.Text = "CAMERA 1 / " + _events[0];
        UpdateS1CameraLabels();
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
        OverviewSensors.Text =
            $"E-STOP AUX DI0: {BoolState(status.EstopOk)}  |  TwinCAT STOP REQUEST: {BoolState(status.GlobalStop)}  |  " +
            $"MD200 RUN DO: {BoolState(status.BeltRun)}  |  ENCODER: {status.BeltSpeed:F2} mm/s";
        string plcQDesc = status.PlcQError ? $"MC ERROR ({status.PlcQErrorId})" : (status.PlcQConnected ? "CONNECTED" : "DISCONNECTED");
        OverviewGantrySensors.Text =
            $"PLC-Q TCP: {plcQDesc}  |  " +
            $"VISION BRIDGE: {BoolState(status.VisionBridgeVerified)}  |  GANTRY BUSY: {BoolState(status.GantryBusy)}" +
            $"  |  ALARM: {BoolState(status.GantryError)}  |  STOP ACK: {BoolState(status.StopCommandConfirmed)}";
        OverviewGlobalAuthority.Text = status.EstopOk && !status.GlobalStop
            ? "ADS LIVE · GLOBAL START/STOP NOT COMMISSIONED; PHYSICAL E-STOP REQUIRED"
            : "PLC STOP REQUESTED / E-STOP AUX NOT OK · DO NOT START EQUIPMENT";
        UpdateStationOverviewVision();
    }

    // Every machine command in this HMI funnels through here. The global START/STOP/RESET
    // contract and any mm/s -> Hz mapping are NOT commissioned, so this method explains the
    // requirement and never transmits an ADS command. It exists so the compact command strip
    // can be shown to operators without any chance of the buttons looking like they "ran" the line.
    private void DashGlobalCommand_Click(object sender, RoutedEventArgs e)
    {
        var action = (sender as Button)?.Tag?.ToString() ?? "UNKNOWN";
        string title = "DATN / Machine Command Information";
        string details = action switch
        {
            "START" => "SYSTEM START is not commissioned. A verified TwinCAT command/acknowledge handshake plus interlocks for the conveyor, Gantry, future SCARA and the physical emergency stop must exist first. This HMI would only raise a start request; the TwinCAT master decides. NO PLC COMMAND WAS SENT.",
            "STOP" => "SYSTEM STOP is not commissioned. A controlled stop sequence that confirms the conveyor, Gantry and future SCARA have actually stopped must be validated first. NO PLC COMMAND WAS SENT.",
            "RESET" => "RESET / fault acknowledge is not commissioned. No verified fault-reset symbol exists in the PLC image for this build, and a reset must never start the machine. NO PLC RESET WAS SENT.",
            "BELT_SET" => "The belt setpoint is mm/s, but the MD200 reference is configured in Hz. Because no validated mm/s to Hz mapping exists, no setpoint is written to the PLC. Provide an accepted calibration first.",
            "BELT_START" => "Manual belt START is locked while the automatic command contract is pending. Manual and automatic commands must never both be live. NO PLC COMMAND WAS SENT.",
            "BELT_STOP" => "Manual belt STOP is locked with the rest of the manual group. In danger, use the PHYSICAL EMERGENCY STOP. NO PLC COMMAND WAS SENT.",
            "ESTOP" => "THIS IS NOT AN EMERGENCY-STOP CONTROL. In danger, use the PHYSICAL E-STOP button on the machine. This UI only monitors auxiliary DI0 feedback and cannot remove hazardous energy.",
            _ => "No command. This HMI transmits no machine command from this control."
        };
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        SensorHistory($"{action}: information only, no ADS command transmitted");
        OverviewRecentEvent.Text = $"{stamp} · HMI REQUEST {action}: NOT SENT TO PLC";
        MessageBox.Show(details, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Explicit mode selection controls visibility, never machine authority.
    // Any mode transition revokes the conveyor arm for fail-closed safety.
    private bool _conveyorCalibrationMode;

    public bool IsConveyorCalibrationMode => _conveyorCalibrationMode;

    private void LineMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isCalibration = LineModeSelector.SelectedIndex == 1;
        if (isCalibration == _conveyorCalibrationMode) return;
        SetConveyorCalibrationMode(isCalibration);
    }
    private void CalibrationArm_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool armed = OvCalibrationArm.IsChecked == true;
        if (DashControlArm.IsChecked != armed)
            DashControlArm.IsChecked = armed;
    }

    public async void SetConveyorCalibrationMode(bool calibrationMode)
    {
        bool wasCalibration = _conveyorCalibrationMode;

        // Mode transition fail-closed: if belt is running when leaving calibration, stop it first before completing mode transition
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));

        // 1. Guarded stop when leaving calibration mode while motion is active
        bool calibMotionActive = _dashStatus?.BeltRun == true || LineMailboxGateway.Instance.ManualBeltActive;
        if (wasCalibration && !calibrationMode && calibMotionActive)
        {
            SensorHistory("MODE TRANSITION: Conveyor running in calibration. Issuing immediate guarded manual stop before exiting to production mode.");
            string transitionMsg = "STOPPING CONVEYOR · MODE TRANSITION PENDING · IN DANGER USE PHYSICAL E-STOP";
            ControlSafetyBanner.Text = transitionMsg;
            if (OvStartReason != null)
            {
                OvStartReason.Text = "MODE TRANSITION PENDING: Stopping conveyor before leaving calibration mode. Controlled stop; in danger use PHYSICAL E-STOP.";
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            if (DashControlNote != null) DashControlNote.Text = transitionMsg;
            if (StatusText != null) StatusText.Text = transitionMsg;

            // Disable start buttons, keep stop buttons enabled while stop is in flight
            if (SysStartButton != null) SysStartButton.IsEnabled = false;
            if (DashStartButton != null) DashStartButton.IsEnabled = false;
            if (SysStopButton != null) SysStopButton.IsEnabled = true;
            if (DashStopButton != null) DashStopButton.IsEnabled = true;

            CommandDispatchResult stopResult = new(false, 12, "Stop not executed");
            try
            {
                stopResult = await LineMailboxGateway.Instance.RequestManualBeltStopAsync(false, adsOnline);
            }
            catch (Exception ex)
            {
                stopResult = new(false, 12, ex.Message);
            }

            if (!stopResult.Success)
            {
                string failMsg = $"MODE TRANSITION STOP FAILED: {stopResult.Message}. Conveyor may still be running. In danger use PHYSICAL E-STOP.";
                SensorHistory(failMsg);
                if (OvStartReason != null)
                {
                    OvStartReason.Text = failMsg;
                    OvStartReason.Foreground = DashBrush("#FF6E70");
                }
                if (DashControlNote != null) DashControlNote.Text = failMsg;
                if (StatusText != null) StatusText.Text = failMsg;
                if (LineModeSelector != null) LineModeSelector.SelectedIndex = 1;
                UpdateControlSafetyBanner();
                RefreshInterlockUi();
                return;
            }

            await RefreshDashboardAsync();
            if (_dashStatus?.BeltRun == true || LineMailboxGateway.Instance.ManualBeltActive)
            {
                string failMsg = "MODE TRANSITION STOP INCOMPLETE: Belt motion still detected active on PLC. In danger use PHYSICAL E-STOP.";
                SensorHistory(failMsg);
                if (OvStartReason != null) { OvStartReason.Text = failMsg; OvStartReason.Foreground = DashBrush("#FF6E70"); }
                if (LineModeSelector != null) LineModeSelector.SelectedIndex = 1;
                UpdateControlSafetyBanner();
                RefreshInterlockUi();
                return;
            }
        }

        // 2. Guarded stop when leaving production AUTO mode while motion is active
        bool autoMotionActive = _dashStatus?.BeltRun == true || LineMailboxGateway.Instance.ManualBeltActive
            || _dashStatus?.GantryBusy == true || _lineInterlock.State is InterlockState.StartRequested or InterlockState.RunningAcknowledged
            || _dashStatus?.LineAutoAccepted == true;
        if (!wasCalibration && calibrationMode && autoMotionActive)
        {
            SensorHistory("MODE TRANSITION: Production line motion active. Issuing immediate guarded line stop before switching to subsystem calibration.");
            string transitionMsg = "STOPPING PRODUCTION LINE · MODE TRANSITION PENDING · IN DANGER USE PHYSICAL E-STOP";
            ControlSafetyBanner.Text = transitionMsg;
            if (OvStartReason != null)
            {
                OvStartReason.Text = "MODE TRANSITION PENDING: Stopping production line before switching to subsystem calibration. Controlled stop; in danger use PHYSICAL E-STOP.";
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            if (DashControlNote != null) DashControlNote.Text = transitionMsg;
            if (StatusText != null) StatusText.Text = transitionMsg;

            if (SysStartButton != null) SysStartButton.IsEnabled = false;
            if (DashStartButton != null) DashStartButton.IsEnabled = false;
            if (SysStopButton != null) SysStopButton.IsEnabled = true;
            if (DashStopButton != null) DashStopButton.IsEnabled = true;

            _lineInterlock.RequestStop(BuildInterlockInputs());
            if (_trackingEnabled) StopTrackingForSourceChange();

            CommandDispatchResult stopResult = new(false, 12, "Line stop not executed");
            try
            {
                stopResult = await LineMailboxGateway.Instance.RequestLineStopAsync(false, adsOnline);
            }
            catch (Exception ex)
            {
                stopResult = new(false, 12, ex.Message);
            }

            if (!stopResult.Success)
            {
                string failMsg = $"MODE TRANSITION STOP FAILED: {stopResult.Message}. Production line may still be running. In danger use PHYSICAL E-STOP.";
                SensorHistory(failMsg);
                if (OvStartReason != null)
                {
                    OvStartReason.Text = failMsg;
                    OvStartReason.Foreground = DashBrush("#FF6E70");
                }
                if (DashControlNote != null) DashControlNote.Text = failMsg;
                if (StatusText != null) StatusText.Text = failMsg;
                if (LineModeSelector != null) LineModeSelector.SelectedIndex = 0;
                UpdateControlSafetyBanner();
                RefreshInterlockUi();
                return;
            }

            await RefreshDashboardAsync();
            if (_dashStatus?.BeltRun == true || LineMailboxGateway.Instance.ManualBeltActive || _dashStatus?.GantryBusy == true || _dashStatus?.LineAutoAccepted == true)
            {
                string failMsg = "MODE TRANSITION STOP INCOMPLETE: Line motion or gantry cycle still detected active on PLC. In danger use PHYSICAL E-STOP.";
                SensorHistory(failMsg);
                if (OvStartReason != null) { OvStartReason.Text = failMsg; OvStartReason.Foreground = DashBrush("#FF6E70"); }
                if (LineModeSelector != null) LineModeSelector.SelectedIndex = 0;
                UpdateControlSafetyBanner();
                RefreshInterlockUi();
                return;
            }
        }

        // 3. Fail-closed guard: never commit mode change while running
        if (_dashStatus?.BeltRun == true || LineMailboxGateway.Instance.ManualBeltActive || _dashStatus?.GantryBusy == true)
        {
            string stillRunningMsg = "MODE TRANSITION BLOCKED: Motion active. Never commit mode change while machine is moving. In danger use PHYSICAL E-STOP.";
            SensorHistory(stillRunningMsg);
            if (OvStartReason != null) { OvStartReason.Text = stillRunningMsg; OvStartReason.Foreground = DashBrush("#FF6E70"); }
            if (LineModeSelector != null) LineModeSelector.SelectedIndex = wasCalibration ? 1 : 0;
            UpdateControlSafetyBanner();
            RefreshInterlockUi();
            return;
        }

        _conveyorCalibrationMode = calibrationMode;

        // Mode transition revokes conveyor arm for fail-closed safety
        _dashControlArmed = false;
        if (DashControlArm != null)
        {
            DashControlArm.IsChecked = false;
            DashControlArm.IsEnabled = calibrationMode;
        }
        if (OvCalibrationArm != null)
            OvCalibrationArm.IsChecked = false;
        if (ConveyorManualControlPanel != null)
            ConveyorManualControlPanel.IsEnabled = calibrationMode;

        if (CalibrationBadge != null)
            CalibrationBadge.Visibility = calibrationMode ? Visibility.Visible : Visibility.Collapsed;
        if (OvCalibrationArm != null)
            OvCalibrationArm.Visibility = calibrationMode ? Visibility.Visible : Visibility.Collapsed;
        if (OvContractStatus != null)
        {
            OvContractStatus.Text = calibrationMode ? "SUBSYSTEM CALIBRATION" : "CONTRACT PENDING";
            OvContractStatus.Foreground = DashBrush(calibrationMode ? "#27C9EC" : "#F4BB45");
        }
        if (OvCommandCommissionStatus != null)
        {
            OvCommandCommissionStatus.Text = calibrationMode
                ? "CALIBRATION MAILBOX LIVE · ISOLATED (NO ROBOT/GLUE)"
                : "PRODUCTION AUTO · COMMISSIONING PENDING";
            OvCommandCommissionStatus.Foreground = DashBrush(calibrationMode ? "#27C9EC" : "#F4BB45");
        }

        if (SysStartButton != null)
        {
            SysStartButton.Content = calibrationMode ? "CALIB START" : "START";
            SysStartButton.ToolTip = calibrationMode
                ? "Start conveyor for subsystem calibration (1..150 mm/s). Camera is NOT required. Gantry, SCARA, and glue are inhibited."
                : "Production AUTO START: Synchronizes conveyor, vision tracking, FIFO, and robot dispensing. Requires fully commissioned line contract.";
        }
        if (SysStopButton != null)
        {
            SysStopButton.Content = calibrationMode ? "CALIB STOP" : "STOP";
            SysStopButton.ToolTip = calibrationMode
                ? "Stop conveyor subsystem calibration run. Controlled stop; NOT certified emergency stop. In danger use PHYSICAL E-STOP."
                : "Requests the normal coordinated stop (tracking + conveyor request). NOT a certified emergency stop. In danger use PHYSICAL E-STOP.";
        }

        UpdateControlSafetyBanner();
        RefreshInterlockUi();
    }

    public void RevokeConveyorCalibrationArm()
    {
        _dashControlArmed = false;
        if (DashControlArm != null) DashControlArm.IsChecked = false;
        if (OvCalibrationArm != null) OvCalibrationArm.IsChecked = false;
        if (DashStartButton != null) DashStartButton.IsEnabled = false;
        if (SysStartButton != null && _conveyorCalibrationMode) SysStartButton.IsEnabled = false;
        UpdateControlSafetyBanner();
        DisarmPendingGantryMasterRequests();
        RefreshInterlockUi();
    }

    private void MainNavigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainNavigation)) return;
        if (MainNavigation == null || CalibrationWorkspaceTab == null) return;
        if (ReferenceEquals(MainNavigation.SelectedItem, CalibrationWorkspaceTab))
            RevokeConveyorCalibrationArm();
    }

    public void NavigateToCalibrationWorkspace()
    {
        RevokeConveyorCalibrationArm();
        if (CalibrationWorkspaceTab != null && MainNavigation != null && !ReferenceEquals(MainNavigation.SelectedItem, CalibrationWorkspaceTab))
            MainNavigation.SelectedItem = CalibrationWorkspaceTab;
    }

    public void NavigateToAutoProduction()
    {
        if (DashboardTab != null)
            MainNavigation.SelectedItem = DashboardTab;
        DisarmPendingGantryMasterRequests();
    }

    private void OpenCalibrationWorkspace_Click(object sender, RoutedEventArgs e) =>
        NavigateToCalibrationWorkspace();

    private void ReturnToAuto_Click(object sender, RoutedEventArgs e) =>
        NavigateToAutoProduction();

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
