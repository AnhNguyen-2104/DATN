using System.Text;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using PlateVision50.TwinCAT;
using PlateVision50.Trigger;
using PlateVision50.Vision;
using WpfYolo_DotNet.Devices;

namespace PlateVision50;

public partial class MainWindow : System.Windows.Window
{
    private readonly UnifiedCameraService _discovery=new();
    private readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(75)};
    private readonly DispatcherTimer _adsTimer=new() {Interval=TimeSpan.FromMilliseconds(600)};
    private readonly VisionOptions _options=new();
    private VisionOptions _optionsCam2=new() {FixedMmPerPixel=0};
    private OfflineTrayAllocator? _s2TrayAllocator;
    private Camera2ProcessingResult? _lastCam2Result;
    private readonly Dictionary<string,MidlineTracker> _trackers=new();
    private readonly List<string> _events=new();
    private readonly ObservableCollection<TriggerAuditRow> _auditRows=new();
    private readonly Dictionary<string,TriggerAuditRow> _auditByTrack=new();
    private readonly string _auditCsv=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlateVision50_V2","trigger_pipeline.csv");
    private readonly string _themeFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlateVision50_V2","ui_theme.txt");
    private readonly CameraOwnershipRegistry _cameraOwnership=new();
    private readonly StationCameraController _cam1=new(1,"Station 01");
    private readonly StationCameraController _cam2=new(2,"Station 02");
    private long _lastCam1Frame;
    private long _lastCam2Frame;
    private bool _scanBusy1;
    private bool _scanBusy2;
    private bool _trackingEnabled;
    private bool _offlineSimulationEnabled;
    private readonly AdsPreview _ads=new();
    private readonly AdsFifoBenchBridge _bridge=new();
    private readonly CameraPipelineSimulator _offlineQueue=new();
    private readonly string _csvPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlateVision50_V2","detections.csv");
    private readonly string _triggerCsv=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlateVision50_V2","trigger-events.csv");
    private readonly Dictionary<string,DateTime> _lastCsvBySource=new();
    private VideoCapture? _video;
    private bool _paused;
    private bool _bridgeRunning;
    private readonly Queue<(TriggerEvent Event,BeltCoordinate Pose)> _pendingBench=new();
    private bool _adsPolling;
    private bool _benchArmed;
    private CancellationTokenSource? _benchCts;
    private CancellationTokenSource? _singleBenchCts;
    private bool _closing;
    private int _frameNumber;
    private int _triggersTotal;
    private BeltDirection _beltDirection=BeltDirection.YMinus;
    private double _lineFraction=.5;
    private PlateResult _last=PlateResult.None("No image");
    private BeltCoordinate _lastBelt=new(0,0,0);
    private FifoSnapshot? _lastSnapshot;
    private readonly VisionLabelOverlay.LabelPlacementTracker _labelPlacements=new();
    private readonly VisionLabelOverlay.LabelPlacementTracker _labelPlacementsS1=new();
    private string _overlaySourceKey="VIDEO";
    private IReadOnlyList<PlateResult> _overlayDetections=Array.Empty<PlateResult>();
    private IReadOnlyDictionary<int,int> _overlayTrackIds=new Dictionary<int,int>();
    private int _overlayImageWidth;
    private int _overlayImageHeight;


    public MainWindow()
    {
        InitializeComponent();
        Loaded+=(_,_)=>{
            try {
                // Operator surfaces are dark industrial by default; the stored
                // preference may only switch to light when explicitly saved.
                bool dark = !File.Exists(_themeFile) || File.ReadAllText(_themeFile).Trim()!="light";
                if(dark) DarkModeBox.IsChecked=true;
                else { ThemePalette.Set(this,false); RefreshCaptureMode(); }
            } catch(Exception ex) {StatusText.Text="Unable to load theme settings: "+ex.Message;}
        };
        PipelineTable.ItemsSource=_auditRows;
        PipelineHistoryTable.ItemsSource=_auditRows;
        _timer.Tick+=OnTick;
        if (AppHardwareCapabilities.AllowAdsPolling)
        {
            _adsTimer.Tick+=OnAdsTick;
            _adsTimer.Start();
        }
        InitDashboard();
        InitStationOverview();
        InitStationDashboards();
        RefreshVideos();
        ResetDetectionHud();
        RefreshCaptureMode();
        if (AppHardwareCapabilities.IsOfflineCalibration)
        {
            Title = "DATN | OFFLINE CALIBRATION / NO HARDWARE";
            StatusText.Text = "OFFLINE CALIBRATION / NO HARDWARE • ADS & CAMERAS INHIBITED";
            if (CalibrationWorkspaceTab != null)
                MainNavigation.SelectedItem = CalibrationWorkspaceTab;
            if (GantryMasterGateText != null)
                GantryMasterGateText.Text = "OFFLINE CALIBRATION: Zero ADS sockets / writes. Live master enable locked.";
            if (GantryMasterStatusText != null)
                GantryMasterStatusText.Text = "COMMAND ECHO: UNKNOWN (NO SNAPSHOT / OFFLINE)";
            if (GantryMasterOverrideText != null)
                GantryMasterOverrideText.Text = "OVERRIDES: STALE / UNKNOWN (SNAPSHOT INVALID)";
            if (GantryMasterDiagnosticsText != null)
                GantryMasterDiagnosticsText.Text = GantryOfflineDiagnosticsText;
        }
        else
        {
            StatusText.Text="Camera offline | ADS read-only";
            if(Environment.GetCommandLineArgs().Any(a=>a.Equals("--demo",StringComparison.OrdinalIgnoreCase)))
                Dispatcher.BeginInvoke(new Action(()=>OpenVideo(@"C:\Users\aml\Desktop\DEMO\1.avi")));
            // QA preview only: --mock starts the conspicuously labelled TEST ONLY interlock
            // demonstration. It never writes ADS and never commands the conveyor.
            InitCameraSlots();
        }
    }
    private bool HasActiveSource => _video!=null || _cam1.IsOpen;

    private void ResetDetectionHud()
    {
        _last=PlateResult.None("Tracking not started");
        _lastBelt=new BeltCoordinate(0,0,0);
        DetectState.Text=_trackingEnabled?"SEARCHING FOR OBJECTS":"LIVE VIEW • DETECTION OFF";
        CenterX.Text="—"; CenterY.Text="—"; AngleValue.Text="—";
        ConfidenceText.Text="Quality —"; ScaleText.Text="Scale —";
        HoleCoordinates.Text="No tracked product";
    }

    private void UpdateControlSafetyBanner()
    {
        if (_dashStatus?.BeltRun == true)
        {
            ControlSafetyBanner.Text = _conveyorCalibrationMode
                ? "CONVEYOR CALIBRATION RUNNING · ISOLATED BELT RUN · NO GANTRY/GLUE"
                : "CONVEYOR RUNNING · MANUAL RUN ACTIVE · NOT PRODUCTION AUTO";
            return;
        }

        if (_conveyorCalibrationMode)
        {
            if (_dashControlArmed)
                ControlSafetyBanner.Text = "CONVEYOR SUBSYSTEM CALIBRATION ARMED · ISOLATED BELT RUN (NO ROBOT/GLUE)";
            else
                ControlSafetyBanner.Text = "CONVEYOR SUBSYSTEM CALIBRATION · ISOLATED BELT RUN (NO ROBOT/GLUE)";
            return;
        }

        if (_dashControlArmed)
        {
            ControlSafetyBanner.Text = "HMI CONVEYOR ARMED · NO ROBOT";
            return;
        }
        // Distinguish data permission from motion authorization. A live PLC
        // state alone never confers permission to operate the robot.
        ControlSafetyBanner.Text = _benchArmed
            ? "BENCH SIMULATION ONLY · PRODUCTION DISARMED"
            : "PRODUCTION AUTO: DISARMED · COMMISSIONING PENDING";
    }
    private void RefreshCaptureMode()
    {
        UpdateControlSafetyBanner();
        StartTrackingButton.IsEnabled=HasActiveSource && !_trackingEnabled;
        StopTrackingButton.IsEnabled=_trackingEnabled;
        string src=_cam1.IsOpen?"CAMERA LIVE":_video!=null?"VIDEO DEMO":"NO SOURCE";
        TrackingModeText.Text=src+" | DETECTION: "+(_trackingEnabled?"ACTIVE":"OFF");
        bool dark=DarkModeBox.IsChecked==true;
        TrackingModeText.Foreground=new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                _trackingEnabled?(dark?"#89E5BA":"#236247"):(dark?"#F2D392":"#795A25")));
    }

    private void DarkModeChanged(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded)return;
        try {
            bool dark=DarkModeBox.IsChecked==true;
            ThemePalette.Set(this,dark);
            RefreshCaptureMode();
            Directory.CreateDirectory(Path.GetDirectoryName(_themeFile)!);
            File.WriteAllText(_themeFile,dark?"dark":"light");
            StatusText.Text="Theme: "+(dark?"dark":"light");
        }
        catch(Exception ex){StatusText.Text="Theme error: "+ex.Message;}
    }
    private void OfflinePipelineChanged(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded)return; // WPF may raise unchecked during InitializeComponent
        _offlineSimulationEnabled=OfflinePipelineBox.IsChecked==true;
        _offlineQueue.Reset();
        PipelineMockStatus.Text=_offlineSimulationEnabled
            ?"VIRTUAL FIFO ENABLED · no ADS write; each Track ID is counted once."
            :"VIRTUAL FIFO OFF · TwinCAT ADS remains read-only.";
        if(_offlineSimulationEnabled)
        {
            if(BenchQueueBox.IsChecked==true)
            {
                BenchQueueBox.IsChecked=false;
                DisarmBench();
            }
            var s=_offlineQueue.GetSnapshot();
            FifoCount.Text=s.Count.ToString();
            FifoProduct.Text="—";
            FifoProgress.Text="VIRTUAL FIFO READY (0/8) · Awaiting camera/video triggers";
            FifoGluePoints.Text="No active product";
        }
        else
        {
            FifoCount.Text="—";
            FifoProduct.Text="—";
            FifoProgress.Text="PLC not available";
            FifoGluePoints.Text="No PLC tracking";
        }
    }
    private async void CheckLiveReadiness_Click(object sender,RoutedEventArgs e)
    {
        if (!AppHardwareCapabilities.AllowAdsPolling)
        {
            ReadinessStatus.Text="NOT READY: Hardware ADS connection is inhibited in offline calibration mode.";
            return;
        }
        ReadinessStatus.Text="Checking TwinCAT symbols (READ ONLY)...";
        try {
            var plc=await Task.Run(()=>_bridge.InspectLiveReadiness());
            bool frameReady=_cam1.IsOpen && _cam1.Model.IsLive(DateTime.UtcNow,TimeSpan.FromSeconds(3));
            bool scaleReady=_options.FixedMmPerPixel>0;
            ReadinessStatus.Text=plc+" | Camera="+(frameReady?"LIVE":"UNVERIFIED")+
               " | Scale mm/px="+(scaleReady?"ENTERED (NOT COMMISSIONED)":"MISSING")+
               " | STATE: LIVE · PLC WRITES NOT AUTHORIZED.";
        }
        catch(Exception ex) {ReadinessStatus.Text="NOT READY: "+ex.Message;}
    }
    private void TestOfflinePipeline_Click(object sender,RoutedEventArgs e)
    {
        try {
            var test=new CameraPipelineSimulator(200,0,85,0,40,40);
            var r=test.Accept("SELFTEST/1",0,0,0,100);
            if(r.ProductId!=1 || r.QueueCount!=1 || r.Corners.Length!=4 ||
               Math.Abs(r.Corners[0].X-(-180))>1e-7 ||
               Math.Abs(r.Corners[0].Y-105)>1e-7)
                throw new InvalidOperationException("Invalid PLC corner transform.");
            var next=test.GetHead(125);
            if(next is null || Math.Abs(next.RobotX-(-175))>1e-7 ||
               Math.Abs(next.Corners[0].X-(-155))>1e-7)
                throw new InvalidOperationException("Encoder propagation mismatch.");
            bool duplicateRejected=false;
            try {test.Accept("SELFTEST/1",0,0,0,100);}
            catch(InvalidOperationException){duplicateRejected=true;}
            if(!duplicateRejected)throw new InvalidOperationException("Duplicate Track ID accepted.");

            // 1. Workspace arrival check at encoder 320mm (+220mm travel -> X = +20mm)
            if(!test.IsHeadInsideWorkspace(320.0))
                throw new InvalidOperationException("Head product should be inside workspace at encoder 320mm.");
            var inWorkSnap = test.GetSnapshot(320.0, 100.0);
            if(!inWorkSnap.Inside || Math.Abs(inWorkSnap.HeadXmm - 20.0) > 1e-6)
                throw new InvalidOperationException("In-work snapshot mismatch.");

            // 2. TwinCAT QD75 Command contract for all 4 vertices (D100..D114)
            for(int v = 1; v <= 4; v++)
            {
                var cmd = test.BuildQd75Command(v, 100.0, 320.0);
                if(!cmd.IsValid || cmd.ErrorCode != 0)
                    throw new InvalidOperationException($"Vertex {v} command rejected: error {cmd.ErrorCode}");
                var words = cmd.ToMelsecWords();
                if(words.Length != 15)
                    throw new InvalidOperationException($"Melsec words length {words.Length} != 15");
            }
            var v1Cmd = test.BuildQd75Command(1, 100.0, 320.0);
            if(v1Cmd.TargetX_0_1um != 520000 || v1Cmd.TargetY_0_1um != 1050000 ||
               v1Cmd.SpeedApproach_0_01mmMin != 3000000 || v1Cmd.SpeedSync_0_01mmMin != 600000 ||
               v1Cmd.DeltaXSync_0_1um != 60000 || v1Cmd.TargetZDown_0_1um != 40000)
                throw new InvalidOperationException("Vertex 1 QD75 parameters mismatch TwinCAT contract.");

            // 3. Boundary rejection check (upstream encoder 100mm -> outside workspace)
            var earlyCmd = test.BuildQd75Command(1, 100.0, 100.0);
            if(earlyCmd.IsValid || earlyCmd.ErrorCode != 501)
                throw new InvalidOperationException("Early upstream command should fail with error 501.");

            // 4. Complete head product
            test.CompleteHead();
            if(test.QueueCount != 0)
                throw new InvalidOperationException("Queue not empty after CompleteHead.");

            // Update UI widgets
            FifoCount.Text = inWorkSnap.Count.ToString();
            FifoProduct.Text = inWorkSnap.HeadProduct.ToString();
            FifoProgress.Text = $"SIMULATED 4-VERTEX TEST PASSED\nTarget: X={v1Cmd.TargetX_mm:F1} mm, Y={v1Cmd.TargetY_mm:F1} mm | V_sync=100.0 mm/s\nQD75 Words: D100=520000, D102=1050000, D104=40000 (Z_down)";
            FifoGluePoints.Text = string.Join("\n", inWorkSnap.GluePoints.Select((p, i) => $"P{i+1}  X={p.X,8:F2}  Y={p.Y,8:F2} mm"));

            PipelineMockStatus.Text = "PASS: Trigger → FIFO → 4 corners → TwinCAT QD75 contract (D100..D114 15 words) verified; PC memory only, 0 ADS writes.";
            StatusText.Text = "OFFLINE TEST PASS: Trigger → FIFO → TwinCAT QD75 coordinate contract verified (0 ADS writes).";
        }
        catch(Exception ex){PipelineMockStatus.Text="FAIL: "+ex.Message;}
    }

    private void DisarmBench()
    {
        // An image START/STOP event must NEVER authorize a PLC write.
        _benchArmed=false;
        UpdateControlSafetyBanner();
        _singleBenchCts?.Cancel(); // Stop/source switch cancels the one-shot too.
        _benchCts?.Cancel();
        _benchCts?.Dispose();
        _benchCts=null;
        _pendingBench.Clear();
        BenchQueueBox.IsChecked=false;
    }

    private void StopTrackingForSourceChange()
    {
        _trackingEnabled=false;
        _trackers.Clear();
        DisarmBench();
        _labelPlacements.Reset();
        _overlayDetections=Array.Empty<PlateResult>();
        PlateOverlay.Children.Clear();
        ResetDetectionHud();
        RefreshCaptureMode();
    }

    private void StartTracking_Click(object sender,RoutedEventArgs e)
    {
        if(!HasActiveSource)
        {
            StatusText.Text="Connect a camera or open a video before START.";
            return;
        }
        // Fresh identity/trigger state prevents phantom crossings from stale preview.
        _trackers.Clear();
        _labelPlacements.Reset();
        _lastCsvBySource.Clear();
        _auditRows.Clear();
        _auditByTrack.Clear();
        _events.Clear();
        _offlineQueue.Reset();
        PipelineMockStatus.Text=_offlineSimulationEnabled?"Virtual FIFO is ready for new tracks.":"Virtual FIFO is disabled.";
        _triggersTotal=0;
        TriggerCounter.Text="0";
        TriggerLog.Text="Detection started: tracks trigger when centers cross the 50% FOV line.";
        DisarmBench(); // image processing only; TwinCAT ADS stays read-only
        _trackingEnabled=true;
        _paused=false;
        ResetDetectionHud();
        RefreshCaptureMode();
        StatusText.Text="START: camera/video detection, tracking and 50% FOV trigger | ADS READ ONLY.";
    }

    private void StopTracking_Click(object sender,RoutedEventArgs e)
    {
        _trackingEnabled=false;
        _trackers.Clear();
        DisarmBench();
        _offlineQueue.Reset();
        _labelPlacements.Reset();
        _overlayDetections=Array.Empty<PlateResult>();
        PlateOverlay.Children.Clear();
        ResetDetectionHud();
        RefreshCaptureMode();
        StatusText.Text="STOP: detection and triggers disabled; camera LIVE VIEW remains connected.";
    }

    private void PreviewOnly(Mat image,string source)
    {
        if(image.Empty()) return;
        using var view=image.Clone();
        DrawMidline(view);
        var bmp=BitmapSourceConverter.ToBitmapSource(view);
        bmp.Freeze();
        Preview.Source=bmp;
            StationFrameReceived();
        PlateOverlay.Children.Clear();
        _overlayDetections=Array.Empty<PlateResult>();
        FrameInfo.Text=source+"  |  "+image.Width+"x"+image.Height+"  |  LIVE VIEW / DETECTION OFF";
    }

    // Visible navigation: LINE OVERVIEW, STATION 01, STATION 02, SHARED CONVEYOR,
    // SETTINGS, ALARMS & LOGS. VISION WORKSPACE and CAMERA / SOURCE are QA-only and
    // collapsed from the toolbar; the camera preparation lives in each station tab.
    private void OpenSettingsPage_Click(object sender,RoutedEventArgs e)
    {
        MainNavigation.SelectedItem=SettingsTab;
    }
    private void OpenMonitorPage_Click(object sender,RoutedEventArgs e)
    {
        MainNavigation.SelectedItem=GantryDetailTab;
    }
    private void OpenVisionWorkspace_Click(object sender,RoutedEventArgs e)
    {
        MainNavigation.SelectedItem=GantryDetailTab;
    }
    private void OpenQaSources_Click(object sender,RoutedEventArgs e)
    {
        // Hidden QA route: local video / SDK enumeration only, never a line command.
        MainNavigation.SelectedItem=CameraSourceTab;
        StatusText.Text="QA SOURCE PICKER: camera/video setup only. Not part of normal line operation.";
    }

    private void RefreshVideos()
    {
        string folder=@"C:\Users\aml\Desktop\DEMO";
        VideoPicker.ItemsSource=Directory.Exists(folder)?Directory.GetFiles(folder,"*.avi").OrderBy(p=>p).Select(p=>new FileInfo(p)).ToList():new List<FileInfo>();
        if(VideoPicker.Items.Count>0)VideoPicker.SelectedIndex=0;
    }
    private void ResetVideoSession()
    {
        // A video loop reuses Track ID #1, #2, ...; never reuse their pending
        // PLC handshakes or local FIFO queue from the previous playback.
        DisarmBench();
        if(_trackers.TryGetValue("DEMO",out var tracker))tracker.Reset();
        _lastCsvBySource.Remove("DEMO");
        _auditByTrack.Clear();
        _offlineQueue.Reset();
        _labelPlacements.Reset();
        _overlayDetections=Array.Empty<PlateResult>();
        PlateOverlay.Children.Clear();
        _frameNumber=0;
        ResetDetectionHud();
    }
    private void OpenDemo_Click(object sender,RoutedEventArgs e)
    {
        if(VideoPicker.SelectedItem is FileInfo f)OpenVideo(f.FullName);
        else StatusText.Text="Missing video files in C:\\Users\\aml\\Desktop\\DEMO";
    }
    private void OpenFile_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog {Filter="Images / videos|*.avi;*.mp4;*.mkv;*.png;*.jpg;*.jpeg;*.bmp|All|*.*"};
        if(dialog.ShowDialog()!=true)return;
        var ext=Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if(new[]{".png",".jpg",".jpeg",".bmp"}.Contains(ext))
        {
            StopTrackingForSourceChange();
            StopVideo();ResetVideoSession();
            using var frame=Cv2.ImRead(dialog.FileName);
            PreviewOnly(frame,Path.GetFileName(dialog.FileName));
            RefreshCaptureMode();
            StatusText.Text="Still image loaded; motion tracking requires a camera or video.";
        }
        else OpenVideo(dialog.FileName);
    }
    private void OpenVideo(string path)
    {
        StopTrackingForSourceChange();
        StopVideo();ResetVideoSession();
        DisconnectSlot(1);
        try
        {
            _video=new VideoCapture(path);
            if(!_video.IsOpened())throw new IOException("Could not read AVI file");
            _paused=false;
            _timer.Start();
            RefreshCaptureMode();
            MainNavigation.SelectedItem=GantryDetailTab;
            StatusText.Text="VIDEO PLAYBACK: "+Path.GetFileName(path)+" | press DETECT ON for tracking. Image-only test, no encoder sync.";
        }
        catch(Exception ex){StopVideo();RefreshCaptureMode();StatusText.Text="VIDEO: "+ex.Message;}
    }
    private void Pause_Click(object sender,RoutedEventArgs e)
    {
        if(_video==null){StatusText.Text="Pause is available for offline video only; live camera streaming continues.";return;}
        _paused=!_paused;
        StatusText.Text=_paused?"DEMO: video paused":"DEMO: video resumed";
    }
    private async void ScanCameras_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            StatusText.Text="Scanning Station 01 Basler USB camera...";
            CameraScanResult result=await Task.Run(()=>_cam1.Scan(CameraTransportMode.Usb));
            CameraPicker.ItemsSource=result.Devices;
            if(result.Devices.Count>0)
            {
                CameraPicker.SelectedIndex=0;
            }

            StatusText.Text="Camera: "+result.Summary+
                (result.HasIssues?" | "+result.Issues[0].Label:"");
        }
        catch(Exception ex){StatusText.Text="Scan error: "+ex.Message;}
    }
    private bool _cameraUiReady;
    private readonly bool[] _scannedSlots=new bool[3];

    private CameraSlotModel SlotModel(int slot)=>slot==1?_cam1.Model:_cam2.Model;
    private StationCameraController SlotController(int slot)=>slot==1?_cam1:_cam2;
    private TextBox SlotIpBox(int slot)=>slot==1?SettingsS1CamIpBox:SettingsS2CamIpBox;
    private TextBox SlotNicBox(int slot)=>slot==1?SettingsS1HostIpBox:SettingsS2HostIpBox;
    private Button SlotScanButton(int slot)=>slot==1?SettingsS1ScanButton:SettingsS2ScanButton;
    private Button SlotConnectButton(int slot)=>slot==1?SettingsS1ConnectButton:SettingsS2ConnectButton;
    private Button SlotDisconnectButton(int slot)=>slot==1?SettingsS1DisconnectButton:SettingsS2DisconnectButton;
    private TextBlock SlotStateText(int slot)=>slot==1?S1CamLinkText:S2CamLinkText;
    private FrameworkElement SlotEmptyText(int slot)=>slot==1?S1CamEmpty:S2CamEmpty;

    private static int ModeIndex(CameraTransportMode mode)=>
        mode==CameraTransportMode.Usb?1:0;

    private void InitCameraSlots()
    {
        _cameraUiReady=false;
        ApplySavedSlot(1);ApplySavedSlot(2);
        _cameraUiReady=true;
        UpdateCameraSlotUi(1);UpdateCameraSlotUi(2);
    }

    private void ApplySavedSlot(int slot)
    {
        var saved=CameraSlotSettingsStore.Load(slot);
        var model=SlotModel(slot);
        model.Mode=saved.Mode;
        model.IpText=saved.Ip;
        SlotIpBox(slot).Text=saved.Ip;
        SlotNicBox(slot).Text=saved.Nic;
        if(slot==1&&SettingsS1CamIpBox!=null)
        {
            if(!string.IsNullOrWhiteSpace(saved.DeviceKey))model.SelectDevice(saved.DeviceKey);
        }
        else if(slot==2&&SettingsS2CamIpBox!=null)
        {
        }
    }

    private void SaveSlot(int slot)
    {
        var model=SlotModel(slot);
        string vendor=CameraSlotDefaults.DefaultVendor(slot);
        CameraTransportMode mode=model.Mode;
        string ipText=SlotIpBox(slot).Text.Trim();
        string nicText=SlotNicBox(slot).Text.Trim();

        var existing=CameraSlotSettingsStore.Load(slot);
        var (deviceKey, resolvedIp, resolvedNic)=CameraSlotDefaults.ResolveKeyForSave(
            slot, vendor, model.Selected, ipText, nicText, existing.DeviceKey, existing.Ip, existing.Nic);

        CameraSlotSettingsStore.Save(slot,mode,resolvedIp,
            resolvedNic,deviceKey,vendor);
    }
    private void BindSlotDevices(int slot)
    {
        if(slot==1&&SettingsS1DeviceBox!=null)
        {
            var model=SlotModel(1);
            SettingsS1DeviceBox.ItemsSource=null;
            SettingsS1DeviceBox.ItemsSource=model.FilteredDevices;
            if(model.Selected!=null)
                SettingsS1DeviceBox.SelectedItem=model.Selected;
        }
    }

    private void SettingsS1DeviceChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_cameraUiReady||_closing)return;
        if(SettingsS1DeviceBox?.SelectedItem is not CameraDevice device)return;
        _cam1.Model.SelectDevice(device.Key);
        SaveSlot(1);
        UpdateCameraSlotUi(1);
    }
    private void SettingsS1IpTextChanged(object sender,TextChangedEventArgs e)
    {
        if(!_cameraUiReady||_closing)return;
        _cam1.Model.IpText=SettingsS1CamIpBox.Text.Trim();
        UpdateCameraSlotUi(1);
    }
    private void SettingsS1NicTextChanged(object sender,TextChangedEventArgs e)
    {
        if(!_cameraUiReady||_closing)return;
        UpdateCameraSlotUi(1);
    }
    private void SettingsS2IpTextChanged(object sender,TextChangedEventArgs e)
    {
        if(!_cameraUiReady||_closing)return;
        _cam2.Model.IpText=SettingsS2CamIpBox.Text.Trim();
        UpdateCameraSlotUi(2);
    }
    private void SettingsS2NicTextChanged(object sender,TextChangedEventArgs e)
    {
        if(!_cameraUiReady||_closing)return;
        UpdateCameraSlotUi(2);
    }

    private void SettingsS1ConnectCamera_Click(object sender,RoutedEventArgs e)
    {
        ConnectSlot(1);
    }
    private void SettingsS2ConnectCamera_Click(object sender,RoutedEventArgs e)
    {
        ConnectSlot(2);
    }
    private void SettingsS1DisconnectCamera_Click(object sender,RoutedEventArgs e)=>DisconnectSlot(1);
    private void SettingsS2DisconnectCamera_Click(object sender,RoutedEventArgs e)=>DisconnectSlot(2);
    private async void SettingsS1Scan_Click(object sender,RoutedEventArgs e)=>await ScanSlotAsync(1);
    private async void SettingsS2Scan_Click(object sender,RoutedEventArgs e)=>await ScanSlotAsync(2);

    private void SettingsS1Save_Click(object sender,RoutedEventArgs e)
    {
        _cam1.Model.IpText=SettingsS1CamIpBox.Text.Trim();
        SaveSlot(1);
        UpdateCameraSlotUi(1);
        StatusText.Text="CAM 1 settings saved to profile";
    }
    private void SettingsS2Save_Click(object sender,RoutedEventArgs e)
    {
        _cam2.Model.IpText=SettingsS2CamIpBox.Text.Trim();
        SaveSlot(2);
        UpdateCameraSlotUi(2);
        StatusText.Text="CAM 2 settings saved to profile";
    }


    private async void S1Scan_Click(object sender,RoutedEventArgs e)=>await ScanSlotAsync(1);
    private async void S2Scan_Click(object sender,RoutedEventArgs e)=>await ScanSlotAsync(2);

    private async Task ScanSlotAsync(int slot)
    {
        var controller=SlotController(slot);
        var model=controller.Model;
        if (!AppHardwareCapabilities.AllowCameraHardware)
        {
            model.MarkOffline();
            StatusText.Text = "CAM " + slot + ": SCAN INHIBITED IN OFFLINE CALIBRATION MODE";
            UpdateCameraSlotUi(slot);
            return;
        }
        if(slot==1?_scanBusy1:_scanBusy2)return;
        if(slot==1)_scanBusy1=true;else _scanBusy2=true;
        int generation=model.ScanGeneration;
        CameraTransportMode mode=model.Mode;
        model.IpText=SlotIpBox(slot).Text.Trim();
        model.MarkScanning();
        UpdateCameraSlotUi(slot);
        try
        {
            string typedIp=SlotIpBox(slot).Text.Trim();
            string nic=SlotNicBox(slot).Text.Trim();
            CameraScanResult result=await Task.Run(()=>controller.Scan(mode,typedIp,nic));

            // Mode changed while the SDK scan was running: the result is stale by definition.
            if(model.ScanGeneration!=generation||model.Mode!=mode)return;

            var devices=result.Devices.ToList();
            string slotVendor=CameraSlotDefaults.DefaultVendor(slot);
            if(CameraTransportPolicy.IsHikVendor(slotVendor)&&mode==CameraTransportMode.Ip&&CameraTransportPolicy.IsValidAddress(typedIp))
            {
                var manual=ManualHikDevice(typedIp);
                if(devices.All(d=>d.Key!=manual.Key))devices.Add(manual);
            }

            model.SetScanResult(new CameraScanResult(devices,result.Issues));
            _scannedSlots[slot]=true;
            BindSlotDevices(slot);

            if(model.Selected is null&&model.FilteredDevices.Count>0)
            {
                var chosen=model.AutoSelectForSlot(slot, slotVendor);
                if(chosen is not null)
                {
                    if(mode==CameraTransportMode.Ip&&!string.IsNullOrWhiteSpace(chosen.Address))
                    {
                        SlotIpBox(slot).Text=chosen.Address;
                        model.IpText=chosen.Address;
                        if(slot==1&&SettingsS1CamIpBox!=null)SettingsS1CamIpBox.Text=chosen.Address;
                        else if(slot==2&&SettingsS2CamIpBox!=null)SettingsS2CamIpBox.Text=chosen.Address;
                    }
                }
            }

            if(devices.Count==0)
            {
                if(result.HasIssues)model.MarkError(result.Issues[0].Summary);
                else model.MarkOffline();
            }
            else model.MarkScanComplete();

            StatusText.Text="CAM "+slot+": "+model.StatusText+
                (result.HasIssues?" | "+result.Issues[0].Label:"");
        }
        catch(Exception ex){model.MarkError(ex.Message);}
        finally
        {
            if(slot==1)_scanBusy1=false;else _scanBusy2=false;
            UpdateCameraSlotUi(slot);
        }
    }

    private static CameraDevice ManualHikDevice(string ip)=>new(
        "Hikvision","GigE","Hikrobot (manual IP)","IP:"+ip,ip,-1);

    private void S1ConnectCamera_Click(object sender,RoutedEventArgs e)=>ConnectSlot(1);
    private void S2ConnectCamera_Click(object sender,RoutedEventArgs e)=>ConnectSlot(2);

    private void ConnectSlot(int slot)
    {
        if (!AppHardwareCapabilities.AllowCameraHardware)
        {
            StatusText.Text = "CAM " + slot + ": CONNECT INHIBITED IN OFFLINE CALIBRATION MODE";
            return;
        }
        var controller=SlotController(slot);
        var model=controller.Model;
        model.IpText=SlotIpBox(slot).Text.Trim();

        string slotVendor=CameraSlotDefaults.DefaultVendor(slot);
        if(CameraTransportPolicy.IsHikVendor(slotVendor)&&
           model.Mode==CameraTransportMode.Ip&&CameraTransportPolicy.IsValidAddress(model.IpText)&&
           (model.Selected is null ||
            model.Selected.Address != model.IpText ||
            CameraTransportPolicy.IsBaslerVendor(model.Selected.Vendor)))
        {
            var manual=ManualHikDevice(model.IpText);
            var merged=model.Devices.Where(d=>d.BackendIndex>=0).Append(manual).ToList();
            model.SetDevices(merged);
            if(!model.SelectDevice(manual.Key))
            {
                StatusText.Text=model.LastError;
                UpdateCameraSlotUi(slot);
                return;
            }
            BindSlotDevices(slot);
        }

        var (ok,error)=model.ValidateBeforeConnect(_cameraOwnership);
        if(!ok){StatusText.Text=error;UpdateCameraSlotUi(slot);return;}

        StopTrackingForSourceChange();
        if(slot==1)StopVideo();

        string nic=SlotNicBox(slot).Text.Trim();
        string resolvedNic=CameraTransportPolicy.ResolveCameraNic(model.IpText, nic);
        if(string.IsNullOrWhiteSpace(nic)&&!string.IsNullOrWhiteSpace(resolvedNic))
        {
            SlotNicBox(slot).Text=resolvedNic;
            nic=resolvedNic;
            if(slot==1&&SettingsS1HostIpBox!=null)SettingsS1HostIpBox.Text=resolvedNic;
            else if(slot==2&&SettingsS2HostIpBox!=null)SettingsS2HostIpBox.Text=resolvedNic;
        }
        if(!controller.Connect(model.Selected!,model.Mode,model.IpText,nic,_cameraOwnership,out error))
        {
            StatusText.Text=error;
            UpdateCameraSlotUi(slot);
            return;
        }

        _paused=false;_timer.Start();
        if(slot==1)
        {
            if(model.Mode==CameraTransportMode.Usb)
            {
                CameraIpBox.Text="N/A (USB)";
                HostIpBox.Text="N/A (USB)";
            }
            else
            {
                CameraIpBox.Text=model.IpText;
                HostIpBox.Text=nic;
            }
            ResetDetectionHud();
        }
        UpdateCameraSlotUi(slot);
        SaveSlot(slot);
        StatusText.Text="CAM "+slot+" CONNECTED — WAITING FOR FRAME";
    }

    private void S1DisconnectCamera_Click(object sender,RoutedEventArgs e)=>DisconnectSlot(1);
    private void S2DisconnectCamera_Click(object sender,RoutedEventArgs e)=>DisconnectSlot(2);

    private void DisconnectSlot(int slot)
    {
        StopTrackingForSourceChange();
        SlotController(slot).Disconnect(_cameraOwnership);
        if(slot==1)
        {
            _lastCam1Frame=0;
            _stationLastFrameUtc=DateTime.MinValue;
            Preview.Source=null;
            if(_video==null)FrameInfo.Text="No source";
        }
        else
        {
            _lastCam2Frame=0;
            Station2Preview.Source=null;
            Ov2CamImage.Source=null;
        }
        if(_video==null&&!_cam1.IsOpen&&!_cam2.IsOpen)_timer.Stop();
        UpdateCameraSlotUi(slot);
        SaveSlot(slot);
        StatusText.Text="CAM "+slot+" OFFLINE";
        UpdateStationOverviewVision();
    }

    private void S1VisionStart_Click(object sender,RoutedEventArgs e)
    {
        StartTracking_Click(sender,e);
        UpdateCameraSlotUi(1);
    }
    private void S1VisionStop_Click(object sender,RoutedEventArgs e)
    {
        StopTracking_Click(sender,e);
        UpdateCameraSlotUi(1);
    }

    private void UpdateS1CameraLabels()=>UpdateCameraSlotUi(1);

    private void UpdateCameraSlotUi(int slot)
    {
        if(!_cameraUiReady||_closing)return;
        var controller=SlotController(slot);
        var model=controller.Model;
        bool open=controller.IsOpen;
        bool fresh=model.IsLive(DateTime.UtcNow,TimeSpan.FromSeconds(3));
        bool videoFresh=slot==1&&_video!=null&&
            DateTime.UtcNow-_stationLastFrameUtc<=TimeSpan.FromSeconds(3);
        bool shownFresh=fresh||videoFresh;
        bool scannedEmpty=_scannedSlots[slot]&&model.Devices.Count==0;
        bool hasIssues=model.ScanIssues.Count>0;

        string state=model.DisplayState;
        if(!open&&scannedEmpty)
            state=hasIssues?"ERROR":model.Mode==CameraTransportMode.Usb?"NO USB CAMERA":"NO IP CAMERA";
        else if(!open&&hasIssues&&!shownFresh)state="SCAN ERR";
        if(videoFresh)state="VIDEO";
        var color=DashBrush(shownFresh?"#39C7A2":
            hasIssues||model.State==CameraSlotState.Error||model.State==CameraSlotState.Lost?"#FF6E70":
            model.State==CameraSlotState.Offline?"#92AABE":"#F4BB45");
        bool isLive=slot==1?shownFresh:fresh;
        string headerState=isLive?"LIVE":"OFFLINE";
        var headerColor=DashBrush(isLive?"#39C7A2":"#92AABE");
        SlotStateText(slot).Text=headerState;
        SlotStateText(slot).Foreground=headerColor;
        SlotScanButton(slot).IsEnabled=!open&&(slot==1?!_scanBusy1:!_scanBusy2);
        SlotDisconnectButton(slot).IsEnabled=open;

        string slotVendor=CameraSlotDefaults.DefaultVendor(slot);
        string typedIp=SlotIpBox(slot).Text.Trim();
        string typedNic=SlotNicBox(slot).Text.Trim();
        bool manualEligible=CameraTransportPolicy.CanDispatchManualHik(model.Mode,slotVendor,typedIp,typedNic);

        // CONNECT is offered for a selected dispatchable device, OR for fresh manual Hikrobot IP + explicit NIC without scan.
        bool canConnect=!open&&(
            (model.Selected is not null&&CameraTransportPolicy.CanDispatch(model.Selected,model.Mode,out _))||
            manualEligible);
        SlotConnectButton(slot).IsEnabled=canConnect;
        if(slot==1)
        {
            if(SettingsS1CamLinkText!=null)
            {
                SettingsS1CamLinkText.Text=state;
                SettingsS1CamLinkText.Foreground=color;
            }
            if(SettingsS1ConnectButton!=null)SettingsS1ConnectButton.IsEnabled=canConnect;
            if(SettingsS1DisconnectButton!=null)SettingsS1DisconnectButton.IsEnabled=open;
            if(SettingsS1ScanButton!=null)SettingsS1ScanButton.IsEnabled=!open&&!_scanBusy1;
            if(SettingsS1DeviceBox!=null)
            {
                SettingsS1DeviceBox.IsEnabled=!open;
                if(!Equals(SettingsS1DeviceBox.SelectedItem, model.Selected))
                    SettingsS1DeviceBox.SelectedItem=model.Selected;
            }
            if(SettingsS1CamIpBox!=null&&SettingsS1CamIpBox.Text!=typedIp)
                SettingsS1CamIpBox.Text=typedIp;
            if(SettingsS1HostIpBox!=null&&SettingsS1HostIpBox.Text!=typedNic)
                SettingsS1HostIpBox.Text=typedNic;
        }
        else
        {
            if(SettingsS2CamLinkText!=null)
            {
                SettingsS2CamLinkText.Text=state;
                SettingsS2CamLinkText.Foreground=color;
            }
            if(SettingsS2ConnectButton!=null)SettingsS2ConnectButton.IsEnabled=canConnect;
            if(SettingsS2DisconnectButton!=null)SettingsS2DisconnectButton.IsEnabled=open;
            if(SettingsS2ScanButton!=null)SettingsS2ScanButton.IsEnabled=!open&&!_scanBusy2;
            if(SettingsS2CamIpBox!=null&&SettingsS2CamIpBox.Text!=typedIp)
                SettingsS2CamIpBox.Text=typedIp;
            if(SettingsS2HostIpBox!=null&&SettingsS2HostIpBox.Text!=typedNic)
                SettingsS2HostIpBox.Text=typedNic;
        }

        string hint=model.State switch
        {
            CameraSlotState.Error=>model.LastError.Length>0?model.LastError:"SCAN ERROR",
            CameraSlotState.Lost=>"NO FRAME · CONNECT IN SETTINGS",
            CameraSlotState.Scanning=>"SCANNING",
            CameraSlotState.Connecting=>"CONNECTING",
            CameraSlotState.Connected=>"CONNECTED · NO FRAME",
            _=>"OFFLINE · CONNECT IN SETTINGS"
        };
        if(!open&&scannedEmpty)
        {
            if(hasIssues)hint=model.ScanIssues[0].Summary;
            else if(model.Mode==CameraTransportMode.Usb)
                hint="NO USB CAMERA · check USB connection";
            else
                hint="NO IP CAMERA · check camera power/link on the camera NIC or SCAN/CONNECT in Settings";
        }
        if(slot==1)S1CamEmpty.Text=hint;else S2CamHint.Text=hint;
        SlotEmptyText(slot).Visibility=shownFresh?Visibility.Collapsed:Visibility.Visible;


        if(slot==1)
        {
            Station1Preview.Opacity=shownFresh?1:.25;
            Ov1CamImage.Opacity=shownFresh?1:.25;
            Ov1CamEmpty.Text="CAM 1 "+hint;
            Ov1CamEmpty.Visibility=shownFresh?Visibility.Collapsed:Visibility.Visible;
            Ov1CamStatus.Text=fresh?"LIVE":videoFresh?"VIDEO":state;
            S1CamBadge.Text="CAM 1";
        }
        else
        {
            Station2Preview.Visibility=fresh?Visibility.Visible:Visibility.Collapsed;
            Ov2CamImage.Visibility=fresh?Visibility.Visible:Visibility.Collapsed;
            Ov2CamEmpty.Visibility=fresh?Visibility.Collapsed:Visibility.Visible;
            Ov2CamStatus.Text=state;
            S2CamBadge.Text="CAM 2";
        }
    }

    private void S1Overlay_SizeChanged(object sender,SizeChangedEventArgs e)=>RefreshPlateLabels();

    private void ConnectCameraIp_Click(object sender,RoutedEventArgs e)
    {
        if(CameraTransportPolicy.IsBaslerVendor(CameraSlotDefaults.DefaultVendor(1)))
        {
            StatusText.Text="Station 01 Basler camera is USB only: IP connect is not supported. Use Settings to scan USB.";
            return;
        }
        _cam1.Model.Mode=CameraTransportMode.Ip;
        if(SettingsS1CamIpBox!=null)SettingsS1CamIpBox.Text=CameraIpBox.Text.Trim();
        if(SettingsS1HostIpBox!=null)SettingsS1HostIpBox.Text=HostIpBox.Text.Trim();
        _cam1.Model.IpText=CameraIpBox.Text.Trim();
        ConnectSlot(1);
    }
    private void ConnectCamera_Click(object sender,RoutedEventArgs e)
    {
        if(CameraPicker.SelectedItem is CameraDevice device)Connect(device);
        else if(CameraPicker.SelectedItem is CameraInfo info)Connect(CameraInfoToDevice(info));
        else StatusText.Text="Select a scanned camera from the picker.";
    }
    private void ConnectAll_Click(object sender,RoutedEventArgs e)=>ConnectCamera_Click(sender,e);
    private void Connect(CameraInfo info)=>Connect(CameraInfoToDevice(info));

    private void Connect(CameraDevice device)
    {
        if(!device.Transport.Contains("USB",StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text="Station 01 Basler camera is USB only: "+device.Transport+" devices cannot be connected.";
            return;
        }
        var model=_cam1.Model;
        model.Mode=CameraTransportMode.Usb;
        var merged=model.Devices.Where(d=>d.Key!=device.Key).Append(device).ToList();
        model.SetDevices(merged);
        model.SelectDevice(device.Key);
        ConnectSlot(1);
    }

    private static CameraDevice CameraInfoToDevice(CameraInfo info)=>new(
        info.Vendor.ToString(),
        info.Transport==CameraTransportType.Usb?"USB":"GigE",
        info.Name,
        info.SerialNumber,
        "",
        info.BackendIndex);

    private void DisconnectCameras_Click(object sender,RoutedEventArgs e)=>DisconnectSlot(1);

    private void StopVideo()
    {
        _video?.Dispose();_video=null;
        if(!_cam1.IsOpen&&!_cam2.IsOpen)_timer.Stop();
    }
    private void OnTick(object? sender,EventArgs e)
    {
        if(_paused && _video!=null)return;
        try
        {
            DateTime now=DateTime.UtcNow;
            _cam1.RefreshState(now,TimeSpan.FromSeconds(3));
            _cam2.RefreshState(now,TimeSpan.FromSeconds(3));

            if(_video!=null)
            {
                using var frame=new Mat();
                if(!_video.Read(frame)||frame.Empty())
                {
                    // Loop video, but reset identities: no recycled stale IDs.
                    _video.Set(VideoCaptureProperties.PosFrames,0);
                    ResetVideoSession();
                    return;
                }
                string frameLabel="DEMO frame "+(++_frameNumber);
                if(_trackingEnabled)ProcessFrame(frame,frameLabel,true,"DEMO");
                else PreviewOnly(frame,frameLabel);
            }
            else PollCamera1();

            PollCamera2();
            UpdateCameraSlotUi(1);
            UpdateCameraSlotUi(2);
        }
        catch(Exception ex){StatusText.Text="Frame error: "+ex.Message;}
    }

    private void PollCamera1()
    {
        if(!_cam1.IsOpen)return;
        long id=_cam1.FrameNumber;
        if(id==_lastCam1Frame)return;
        using var image=_cam1.CloneLatestFrame();
        if(image is null||image.Empty())return;
        _lastCam1Frame=id;
        _cam1.Model.MarkFrame(DateTime.UtcNow);
        string frameLabel="CAM1 "+(id)+" #"+id;
        string sourceKey="CAM1/"+(_cam1.Model.Selected?.Key??"manual");
        if(_trackingEnabled)ProcessFrame(image,frameLabel,true,sourceKey);
        else PreviewOnly(image,frameLabel);
    }

    private void PollCamera2()
    {
        if(!_cam2.IsOpen)return;
        long id=_cam2.FrameNumber;
        if(id==_lastCam2Frame)return;
        using var image=_cam2.CloneLatestFrame();
        if(image is null||image.Empty())return;
        _lastCam2Frame=id;
        _cam2.Model.MarkFrame(DateTime.UtcNow);

        string sourceKey=Camera2Pipeline.BuildTrackerKey(_cam2.Model.Selected?.Key??_cam2.Model.IpText);
        bool isCalibrated=Camera2Pipeline.IsCalibrationValid(_optionsCam2);

        if(isCalibrated&&_trackingEnabled)
        {
            var tracker=GetTracker(sourceKey);
            var result=Camera2Pipeline.ProcessFrame(
                image,
                sourceKey,
                _optionsCam2,
                _beltDirection,
                tracker,
                _s2TrayAllocator);

            _lastCam2Result=result;
            if(result.PlateDetected)
            {
                S2Detect.Text="PLATE OK";
                S2Confidence.Text=result.Detection.Confidence.ToString("P0");
                S2Track.Text=result.TrackId.HasValue?result.TrackId.Value.ToString():"--";
                S2Pose.Text=result.BeltPose!=null
                    ?$"X {result.BeltPose.Xmm:F1}   Y {result.BeltPose.Ymm:F1}   θ {result.BeltPose.ThetaDeg:F1}°"
                    :"X --   Y --   θ --";
                S2FrameQuality.Text="CAM 2 · VISION ONLY";

                if(result.Allocation!=null&&result.Allocation.Success)
                {
                    S2Vertex.Text=$"Slot #{result.Allocation.Slot?.Index} (R{result.Allocation.Slot?.Row} C{result.Allocation.Slot?.Column})";
                }

                var focus=result.TrackId;
                var indexToId=new Dictionary<int,int>();
                if(result.TrackId.HasValue)indexToId[0]=result.TrackId.Value;
                using var annotated=PlateDetector.DrawAll(image,[result.Detection],_optionsCam2,indexToId,focus);
                var bmp=BitmapSourceConverter.ToBitmapSource(annotated);
                bmp.Freeze();
                Station2Preview.Source=bmp;
                Ov2CamImage.Source=bmp;
            }
            else
            {
                S2Detect.Text="NO PLATE";
                S2Confidence.Text="--";
                S2Track.Text="--";
                S2Pose.Text="X --   Y --   θ --";
                S2FrameQuality.Text="CAM 2 · NO PLATE IN VIEW";
                var bmp=BitmapSourceConverter.ToBitmapSource(image);
                bmp.Freeze();
                Station2Preview.Source=bmp;
                Ov2CamImage.Source=bmp;
            }
        }
        else
        {
            // Fail closed: display and audit NO pose
            _lastCam2Result=null;
            var bmp=BitmapSourceConverter.ToBitmapSource(image);
            bmp.Freeze();
            Station2Preview.Source=bmp;
            Ov2CamImage.Source=bmp;
            S2Track.Text="--";
            S2Detect.Text=isCalibrated?"DETECT OFF":"NOT CALIBRATED";
            S2Confidence.Text="--";
            S2Pose.Text="X --   Y --   θ --";
            S2FrameQuality.Text=isCalibrated
                ?"CAM 2 · ACQUISITION ONLY"
                :"CAM 2 · UNCALIBRATED (FAIL CLOSED)";
        }
    }

    public bool TryApplyStation2OfflineConfig(VisionOptions options,TrayLayoutConfig? trayConfig,out string error)
    {
        if(trayConfig==null)
        {
            error="CAM2 offline tray layout configuration is required (no null tray layout permitted).";
            return false;
        }
        VisionOptions targetOpt=_optionsCam2;
        OfflineTrayAllocator? targetAlloc=_s2TrayAllocator;
        if(!Camera2Pipeline.TryApplyOfflineConfig(ref targetOpt,ref targetAlloc,options,trayConfig,out error))
        {
            return false;
        }

        // Commit all state atomically only after successful construction and validation
        _optionsCam2=targetOpt;
        _s2TrayAllocator=targetAlloc;
        _lastCam2Result=null;
        return true;
    }

    public void ConfigureStation2Calibration(VisionOptions options,TrayLayoutConfig? trayConfig=null)
    {
        if(!TryApplyStation2OfflineConfig(options,trayConfig,out string err))
            throw new ArgumentException(err);
    }

    private void SettingsS2ApplyConfig_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            // All inputs must be non-blank and explicitly specified (no implicit defaults)
            if(string.IsNullOrWhiteSpace(SettingsS2ScaleBox.Text))
                throw new ArgumentException("Scale (mm/px) must not be blank.");
            double scale=GetNumber(SettingsS2ScaleBox.Text,"scale mm/pixel");

            if(string.IsNullOrWhiteSpace(SettingsS2OriginXBox.Text))
                throw new ArgumentException("Origin X (px) must not be blank.");
            double originX=GetNumber(SettingsS2OriginXBox.Text,"originX");

            if(string.IsNullOrWhiteSpace(SettingsS2OriginYBox.Text))
                throw new ArgumentException("Origin Y (px) must not be blank.");
            double originY=GetNumber(SettingsS2OriginYBox.Text,"originY");

            if(string.IsNullOrWhiteSpace(SettingsS2OffsetXBox.Text))
                throw new ArgumentException("Offset X (mm) must not be blank.");
            double offsetX=GetNumber(SettingsS2OffsetXBox.Text,"offsetX");

            if(string.IsNullOrWhiteSpace(SettingsS2OffsetYBox.Text))
                throw new ArgumentException("Offset Y (mm) must not be blank.");
            double offsetY=GetNumber(SettingsS2OffsetYBox.Text,"offsetY");

            if(string.IsNullOrWhiteSpace(SettingsS2HolePitchBox.Text))
                throw new ArgumentException("Hole pitch (mm) must not be blank.");
            double holePitch=GetNumber(SettingsS2HolePitchBox.Text,"hole pitch mm");

            if(string.IsNullOrWhiteSpace(SettingsS2PlateSizeBox.Text))
                throw new ArgumentException("Plate size (mm) must not be blank.");
            double plateSize=GetNumber(SettingsS2PlateSizeBox.Text,"plate size mm");

            if(!Camera2Pipeline.TryCreateCalibrationOptions(scale,originX,originY,offsetX,offsetY,holePitch,plateSize,out var calibOpt,out var calibErr))
                throw new ArgumentException(calibErr);

            // Tray layout parameters are mandatory (no null trayConfig success path)
            if(string.IsNullOrWhiteSpace(SettingsS2TrayRowsBox.Text))
                throw new ArgumentException("Tray rows must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayColsBox.Text))
                throw new ArgumentException("Tray columns must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayPitchXBox.Text))
                throw new ArgumentException("Tray pitch X must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayPitchYBox.Text))
                throw new ArgumentException("Tray pitch Y must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayOriginXBox.Text))
                throw new ArgumentException("Tray origin X must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayOriginYBox.Text))
                throw new ArgumentException("Tray origin Y must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayAngleBox.Text))
                throw new ArgumentException("Tray angle must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayBoundMinXBox.Text))
                throw new ArgumentException("Workspace Min X must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayBoundMaxXBox.Text))
                throw new ArgumentException("Workspace Max X must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayBoundMinYBox.Text))
                throw new ArgumentException("Workspace Min Y must not be blank.");
            if(string.IsNullOrWhiteSpace(SettingsS2TrayBoundMaxYBox.Text))
                throw new ArgumentException("Workspace Max Y must not be blank.");

            if(!int.TryParse(SettingsS2TrayRowsBox.Text.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out int rows)||rows<=0)
                throw new ArgumentException("Tray rows must be a positive integer (> 0).");
            if(!int.TryParse(SettingsS2TrayColsBox.Text.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out int cols)||cols<=0)
                throw new ArgumentException("Tray columns must be a positive integer (> 0).");

            double pitchX=GetNumber(SettingsS2TrayPitchXBox.Text,"tray pitchX");
            double pitchY=GetNumber(SettingsS2TrayPitchYBox.Text,"tray pitchY");
            double trayOx=GetNumber(SettingsS2TrayOriginXBox.Text,"tray originX");
            double trayOy=GetNumber(SettingsS2TrayOriginYBox.Text,"tray originY");
            double trayAngle=GetNumber(SettingsS2TrayAngleBox.Text,"tray angle");
            double minX=GetNumber(SettingsS2TrayBoundMinXBox.Text,"workspace MinX");
            double maxX=GetNumber(SettingsS2TrayBoundMaxXBox.Text,"workspace MaxX");
            double minY=GetNumber(SettingsS2TrayBoundMinYBox.Text,"workspace MinY");
            double maxY=GetNumber(SettingsS2TrayBoundMaxYBox.Text,"workspace MaxY");

            var bounds=new TrayWorkspaceBounds(minX,maxX,minY,maxY);
            if(!bounds.IsValid)
                throw new ArgumentException($"Invalid workspace bounds: MinX({minX:F1}) < MaxX({maxX:F1}) and MinY({minY:F1}) < MaxY({maxY:F1}) required.");

            // Apply atomically: if anything fails, prior state is untouched
            VisionOptions targetOpt=_optionsCam2;
            OfflineTrayAllocator? targetAlloc=_s2TrayAllocator;
            if(!Camera2Pipeline.TryCreateAndApplyOfflineConfig(
                ref targetOpt,ref targetAlloc,
                scale,originX,originY,offsetX,offsetY,holePitch,plateSize,
                rows,cols,pitchX,pitchY,trayOx,trayOy,trayAngle,bounds,
                out var applyErr))
            {
                throw new InvalidOperationException(applyErr);
            }

            _optionsCam2=targetOpt;
            _s2TrayAllocator=targetAlloc;
            _lastCam2Result=null;

            if(SettingsS2CalibSummary!=null)
                SettingsS2CalibSummary.Text=FormattableString.Invariant($"{scale:F4} mm/px (OFFLINE)");
            if(SettingsS2TraySummary!=null&&_s2TrayAllocator!=null)
                SettingsS2TraySummary.Text=FormattableString.Invariant($"{rows}x{cols} ({pitchX}x{pitchY}mm, OFFLINE)");
            StatusText.Text="CAM 2 offline calibration & tray configuration applied successfully.";
        }
        catch(Exception ex)
        {
            StatusText.Text="CAM 2 Config Error: "+ex.Message;
        }
    }

    private void SettingsS2ClearConfig_Click(object sender,RoutedEventArgs e)
    {
        _optionsCam2.FixedMmPerPixel=0;
        _s2TrayAllocator=null;
        _lastCam2Result=null;
        if(SettingsS2CalibSummary!=null)SettingsS2CalibSummary.Text="NOT CONFIGURED (FAIL CLOSED)";
        if(SettingsS2TraySummary!=null)SettingsS2TraySummary.Text="NOT CONFIGURED (FAIL CLOSED)";
        StatusText.Text="CAM 2 reset to fail closed / uncalibrated.";
    }

    public void ResetStation2Tray()
    {
        _s2TrayAllocator?.ResetTray();
    }
    private MidlineTracker GetTracker(string source)
    {
        if(!_trackers.TryGetValue(source,out var t))
        {
            t=new MidlineTracker{Direction=_beltDirection,LineFraction=_lineFraction};
            _trackers.Add(source,t);
        }
        return t;
    }
    private void ProcessFrame(Mat image,string source,bool show=true,string? sourceKey=null)
    {
        if(image.Empty())return;
        string key=sourceKey??"DEMO";
        var detections=PlateDetector.AnalyzeAll(image,_options);
        var tracker=GetTracker(key);
        var tracks=tracker.Update(key,detections,image.Width,image.Height,out var triggered);
        foreach(var ev in triggered)RecordTrigger(ev,image.Width,image.Height);

        if(show)
        {
            int mainIndex=0;
            for(int i=0;i<tracks.Count;i++)
                if(tracks[i].Crossed){mainIndex=i;break;}
            var current=detections.Count>mainIndex?detections[mainIndex]:PlateResult.None("No square in view");
            _last=current;
            if(current.Found)_lastBelt=BeltCoordinates.FromDetection(current,_options,image.Width,image.Height,_beltDirection);
            int? focus=tracks.Count>mainIndex?tracks[mainIndex].ProductId:null;
            var indexToId=tracks.Select((t,i)=>(t,i)).ToDictionary(x=>x.i,x=>x.t.ProductId);
            using var annotated=PlateDetector.DrawAll(image,detections,_options,indexToId,focus);
            DrawMidline(annotated);
            var bmp=BitmapSourceConverter.ToBitmapSource(annotated);bmp.Freeze();Preview.Source=bmp;
            StationFrameReceived();
            _overlayDetections=detections;
            _overlayTrackIds=indexToId;
            _overlaySourceKey=key;
            _overlayImageWidth=image.Width;
            _overlayImageHeight=image.Height;
            RefreshPlateLabels();
            DetectState.Text=detections.Count>0?$"FOUND {detections.Count} plate(s) • {tracks.Count} track(s)":"NO VALID PLATE";
            CenterX.Text=_last.Found?_lastBelt.Xmm.ToString("F2",CultureInfo.InvariantCulture):"—";
            CenterY.Text=_last.Found?_lastBelt.Ymm.ToString("F2",CultureInfo.InvariantCulture):"—";
            AngleValue.Text=_last.Found?_last.AngleDeg.ToString("F2",CultureInfo.InvariantCulture)+"°":"—";
            ConfidenceText.Text=_last.Found?"Quality "+_last.Confidence.ToString("P0"):"Quality —";
            ScaleText.Text=_last.Found?$"{_last.MmPerPixel:F4} mm/px"+(_options.FixedMmPerPixel<=0?" DEMO":""):"Scale —";
            if (!_last.Found || _last.GluePx == null || _last.GluePx.Length == 0)
            {
                HoleCoordinates.Text="No plate";
            }
            else
            {
                bool mappedCanonical = false;
                if (_last.GluePx.Length == 4)
                {
                    try
                    {
                        var vertices = BeltCoordinates.MapVertices(_last.GluePx, _last.AngleDeg, image.Width, image.Height,
                            _last.MmPerPixel, _options.OriginPxX, _options.OriginPxY, _options.OffsetXmm, _options.OffsetYmm, _beltDirection);
                        HoleCoordinates.Text = string.Join("\n", vertices.Select(v => $"P{v.Index}  X={v.X_ConveyorMm,8:F2}  Y={v.Y_ConveyorMm,8:F2}"));
                        mappedCanonical = true;
                    }
                    catch
                    {
                        mappedCanonical = false;
                    }
                }
                if (!mappedCanonical)
                {
                    HoleCoordinates.Text = string.Join("\n", _last.GluePx.Select((p, i) => {
                        var pos = BeltCoordinates.Map(p.X, p.Y, _last.AngleDeg, image.Width, image.Height,
                            _last.MmPerPixel, _options.OriginPxX, _options.OriginPxY, _options.OffsetXmm, _options.OffsetYmm, _beltDirection);
                        return $"P{i+1}  X={pos.Xmm,8:F2}  Y={pos.Ymm,8:F2}";
                    }));
                }
            }
            FrameInfo.Text=source+"  |  "+image.Width+"×"+image.Height+"  |  dir "+_beltDirection;
        }
        if(detections.Count>0 && (!_lastCsvBySource.TryGetValue(key,out var last) || DateTime.UtcNow-last>TimeSpan.FromMilliseconds(250)))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_csvPath)!);
                if(!File.Exists(_csvPath))File.WriteAllText(_csvPath,"utc,source,x_mm,y_mm,theta_deg,scale,quality\n");
                foreach(var p in detections)
                {
                    var pos=BeltCoordinates.FromDetection(p,_options,image.Width,image.Height,_beltDirection);
                    File.AppendAllText(_csvPath,FormattableString.Invariant($"{DateTime.UtcNow:O},{key},{pos.Xmm:F4},{pos.Ymm:F4},{pos.ThetaDeg:F4},{p.MmPerPixel:F6},{p.Confidence:F4}\n"));
                }
                _lastCsvBySource[key]=DateTime.UtcNow;
            }
            catch(Exception ex){StatusText.Text="CSV: "+ex.Message;}
        }
    }
    private void RefreshPlateLabels()
    {
        if(!_trackingEnabled || _overlayImageWidth<=0 || _overlayImageHeight<=0)
        {
            PlateOverlay.Children.Clear();
            S1Overlay.Children.Clear();
            return;
        }
        VisionLabelOverlay.Render(PlateOverlay,_overlayDetections,_overlayTrackIds,_options,
            _beltDirection,_overlayImageWidth,_overlayImageHeight,
            PlateOverlay.ActualWidth,PlateOverlay.ActualHeight,
            _labelPlacements,_overlaySourceKey,_lineFraction);
        // Station 01 shows the same detection overlay over its own viewport; a separate
        // placement tracker prevents the two surfaces from fighting over label offsets.
        if(S1Overlay.ActualWidth>1 && S1Overlay.ActualHeight>1)
            VisionLabelOverlay.Render(S1Overlay,_overlayDetections,_overlayTrackIds,_options,
                _beltDirection,_overlayImageWidth,_overlayImageHeight,
                S1Overlay.ActualWidth,S1Overlay.ActualHeight,
                _labelPlacementsS1,_overlaySourceKey,_lineFraction);
    }
    private void PlateOverlay_SizeChanged(object sender,SizeChangedEventArgs e)
        => RefreshPlateLabels();

    private void DrawMidline(Mat image)
    {
        // The trigger line is a geometric overlay. ALL descriptive text is
        // rendered by WPF, so neither downsampling nor pixel anti-aliasing
        // can blur or overlap workpiece labels.
        var yellow=new Scalar(55,203,255);
        int at=(int)Math.Round((_beltDirection is BeltDirection.XPlus or BeltDirection.XMinus
            ?image.Width:image.Height)*_lineFraction);
        if(_beltDirection is BeltDirection.XPlus or BeltDirection.XMinus)
            Cv2.Line(image,new OpenCvSharp.Point(at,0),new OpenCvSharp.Point(at,image.Height-1),yellow,2);
        else
            Cv2.Line(image,new OpenCvSharp.Point(0,at),new OpenCvSharp.Point(image.Width-1,at),yellow,2);
        BeltDirectionInfo.Text=_beltDirection switch
        {
            BeltDirection.XPlus=>"CONVEYOR → (X+)",
            BeltDirection.XMinus=>"CONVEYOR ← (X−)",
            BeltDirection.YPlus=>"CONVEYOR ↓ (Y+)",
            _=>"CONVEYOR ↑ (Y−)"
        };
    }
    private void AppendPipelineCsv(TriggerAuditRow row,string eventName,string details)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_auditCsv)!);
            static string Esc(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
            if(!File.Exists(_auditCsv))
                File.WriteAllText(_auditCsv,"utc,source,track_id,previous_projected_px,current_projected_px,line_px,x_mm,y_mm,theta_deg,stage,description\n");
            File.AppendAllText(_auditCsv,FormattableString.Invariant(
                $"{DateTime.UtcNow:O},{Esc(row.Source)},{row.TrackId},{row.PreviousPx:F2},{row.CurrentPx:F2},{row.MidlinePx:F2},{row.Xmm:F4},{row.Ymm:F4},{row.Theta:F3},{Esc(eventName)},{Esc(details)}\n"));
        }
        catch(Exception ex){StatusText.Text="Audit CSV error: "+ex.Message;}
    }

    private void RecordTrigger(TriggerEvent trigger,int width,int height)
    {
        if(!_trackingEnabled)return;
        var pose=BeltCoordinates.FromDetection(trigger.Pose,_options,width,height,_beltDirection);
        ConveyorVertex[]? measuredVertices = null;
        if (trigger.Pose.GluePx != null && trigger.Pose.GluePx.Length == 4)
        {
            try {
                measuredVertices = BeltCoordinates.MapVertices(trigger.Pose.GluePx, trigger.Pose.AngleDeg,
                    width, height, trigger.Pose.MmPerPixel, _options.OriginPxX, _options.OriginPxY,
                    _options.OffsetXmm, _options.OffsetYmm, _beltDirection);
            } catch { measuredVertices = null; }
        }
        _triggersTotal++;
        TriggerCounter.Text=_triggersTotal.ToString();
        string line=FormattableString.Invariant($"#{trigger.ProductId}  {trigger.Source}  X={pose.Xmm:F2}  Y={pose.Ymm:F2}  A={pose.ThetaDeg:F2}°");
        string rowKey=trigger.Source+"/"+trigger.ProductId;
        var row=new TriggerAuditRow {
            Timestamp=DateTime.Now, Source=trigger.Source,TrackId=trigger.ProductId,
            PreviousPx=trigger.PreviousProjectedPx,CurrentPx=trigger.CurrentProjectedPx,
            MidlinePx=trigger.MidlinePx,Xmm=pose.Xmm,Ymm=pose.Ymm,Theta=pose.ThetaDeg,
            Delivery=_benchArmed?"TRIGGER → FIFO BENCH":"TRIGGER → NOT SENT",
            Plc=_benchArmed?"WAITING FOR SAFETY / ACK":"ADS READ ONLY"
        };
        _auditRows.Insert(0,row);
        _auditByTrack[rowKey]=row;
        if(_auditRows.Count>250)_auditRows.RemoveAt(_auditRows.Count-1);
        AppendPipelineCsv(row,"MIDLINE_TRIGGER",_benchArmed?"BENCH REQUESTED":"NO ADS WRITE");
        _events.Insert(0,DateTime.Now.ToString("HH:mm:ss")+" | "+line);
        if(_events.Count>4)_events.RemoveAt(_events.Count-1);
        TriggerLog.Text=string.Join("\n",_events);
        if(_offlineSimulationEnabled)
        {
            try {
                // This is in-memory PLC contract simulation, NEVER an ADS transaction.
                var virtualAck=_offlineQueue.Accept(rowKey,pose.Xmm,pose.Ymm,pose.ThetaDeg,0,measuredVertices);
                row.Delivery="VIRTUAL FIFO ACK #"+virtualAck.ProductId;
                row.Plc=measuredVertices != null ? "MEASURED 4 VERTICES - NO PLC WRITE" : "SIMULATED 4 POINTS - NO PLC WRITE";
                string vertexLabel = measuredVertices != null ? "4 vertices: " : "4 points: ";
                PipelineMockStatus.Text=$"VIRTUAL ACK #{virtualAck.ProductId} | FIFO={virtualAck.QueueCount}/8 | {vertexLabel}"+
                    string.Join(" ; ",virtualAck.Corners.Select((p,i)=>$"P{i+1}=({p.X:F1},{p.Y:F1})"));
                AppendPipelineCsv(row,"SIMULATED_FIFO_ACK",PipelineMockStatus.Text);
                var snap=_offlineQueue.GetSnapshot();
                FifoCount.Text=snap.Count.ToString();
                FifoProduct.Text=snap.HeadProduct>0?snap.HeadProduct.ToString():"—";
                FifoProgress.Text=$"VIRTUAL FIFO (PC MEMORY · ZERO ADS)\nHead: X={snap.HeadXmm:F1}, Y={snap.HeadYmm:F1} mm | window={(snap.Inside?"INSIDE":"WAIT")}\nAccepted={snap.Accepted}, Count={snap.Count}/8";
                FifoGluePoints.Text=snap.Count>0?string.Join("\n",snap.GluePoints.Select((p,i)=>$"P{i+1}  X={p.X,8:F2}  Y={p.Y,8:F2} mm")):"No simulated product";
            }
            catch(Exception ex)
            {
                row.Delivery="SIMULATION REJECTED";
                row.Plc=ex.Message;
                PipelineMockStatus.Text="SIM REJECTED: "+ex.Message;
                AppendPipelineCsv(row,"SIMULATED_FIFO_REFUSED",ex.Message);
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_triggerCsv)!);
            if(!File.Exists(_triggerCsv))File.WriteAllText(_triggerCsv,"utc,source,track_id,x_mm,y_mm,theta_deg\n");
            File.AppendAllText(_triggerCsv,FormattableString.Invariant($"{trigger.UtcAtCrossing:O},{trigger.Source},{trigger.ProductId},{pose.Xmm:F4},{pose.Ymm:F4},{pose.ThetaDeg:F4}\n"));
        }catch(Exception ex){StatusText.Text="Trigger log: "+ex.Message;}
        StatusText.Text="MIDLINE CROSS: "+line+(_benchArmed?" | FIFO BENCH pending":" | NO ADS WRITE");
        if(_benchArmed)
        {
            if(_pendingBench.Count>=8) {
                row.Delivery="DROPPED: BENCH FIFO FULL";row.Plc="NOT SENT";
                AppendPipelineCsv(row,"BENCH_REJECT","FIFO pending full");
                AdsStatus.Text="BENCH backlog 8/8; trigger dropped, not written to PLC";return;
            }
            row.Delivery="QUEUED IN C#";
            AppendPipelineCsv(row,"C_SHARP_PENDING","Awaiting guarded ADS handshake");
            _pendingBench.Enqueue((trigger,pose));
            _=PumpBenchQueue();
        }
    }
    private async Task PumpBenchQueue()
    {
        if(_bridgeRunning)return;
        _bridgeRunning=true;
        try
        {
            while(_benchArmed&&_pendingBench.Count>0)
            {
                var item=_pendingBench.Dequeue();
                var evt=item.Event;
                try
                {
                    var rowKey=evt.Source+"/"+evt.ProductId;
                    if(_auditByTrack.TryGetValue(rowKey,out var before)){
                        before.Delivery="ADS BENCH SENT → WAITING FOR PLC ACK";
                        AppendPipelineCsv(before,"ADS_HANDSHAKE_START","Bench simulation only");
                    }
                    var token=_benchCts?.Token??CancellationToken.None;
                    var s=await _bridge.EnqueueBenchAsync(item.Pose.Xmm,item.Pose.Ymm,item.Pose.ThetaDeg,token);
                    AdsStatus.Text=s;
                    if(_auditByTrack.TryGetValue(rowKey,out var okRow)){
                        var snap=await Task.Run(()=>_bridge.ReadStatus());
                        okRow.Delivery="PLC ACK RECEIVED";
                        okRow.Plc=$"Accepted={snap.Accepted}; Queue={snap.Count}; Head={snap.HeadProduct}";
                        AppendPipelineCsv(okRow,"PLC_ACK",okRow.Plc);
                    }
                }
                catch(Exception ex)
                {
                    AdsStatus.Text="FIFO REFUSED for #"+evt.ProductId+": "+ex.Message;
                    if(_auditByTrack.TryGetValue(evt.Source+"/"+evt.ProductId,out var failRow)){
                        failRow.Delivery="ADS NOT ACKNOWLEDGED";
                        failRow.Plc=ex.Message;
                        AppendPipelineCsv(failRow,"ADS_REFUSED",ex.Message);
                    }
                    DisarmBench();
                    break;
                }
            }
        }
        finally{_pendingBench.Clear();_bridgeRunning=false;}
    }
    private void LineDirection_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(!IsLoaded)return;
        ApplyDirection();
    }
    private void ApplyDirection()
    {
        _beltDirection=LineDirectionPicker.SelectedIndex switch
        {1=>BeltDirection.XMinus,2=>BeltDirection.YPlus,3=>BeltDirection.YMinus,_=>BeltDirection.XPlus};
        // Disarm the write path when direction/trigger geometry changes:
        // previously detected poses must never be sent with a new mapping.
        DisarmBench();
        foreach(var tracker in _trackers.Values){tracker.Direction=_beltDirection;tracker.LineFraction=_lineFraction;tracker.Reset();}
        _labelPlacements.Reset();
        _overlayDetections=Array.Empty<PlateResult>();
        PlateOverlay.Children.Clear();
        _auditByTrack.Clear();
        ResetDetectionHud();
        _events.Clear();TriggerLog.Text="Tracking reset: direction or trigger line changed";
        TriggerSettingsSummary.Text=$"{_beltDirection}  ·  {_lineFraction*100:F0}% FOV  · configure in Settings";
        StatusText.Text="Direction "+_beltDirection+" | 50% FOV line. New tracks required.";
    }
    private void ApplyLine_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            double v=GetNumber(LineFractionBox.Text,"Trigger percentage");
            if(v<15||v>85)throw new ArgumentException("Line fraction range 15..85%");
            _lineFraction=v/100.0;
            ApplyDirection();
        }
        catch(Exception ex){StatusText.Text="Trigger line: "+ex.Message;}
    }
    private async void OnAdsTick(object? sender,EventArgs e)
    {
        if(_adsPolling || !AppHardwareCapabilities.AllowAdsPolling)return;
        _adsPolling=true;
        try
        {
            var s=await Task.Run(()=>_bridge.ReadStatus());
            _lastSnapshot=s;
            FifoCount.Text=s.Count.ToString();
            FifoProduct.Text=s.HeadProduct>0?s.HeadProduct.ToString():"—";
            FifoProgress.Text=$"Encoder: {s.EncoderTravelMm:F1} mm | Speed: {s.EncoderMmPerSec:F2} mm/s\nHead: X={s.HeadXmm:F1}, Y={s.HeadYmm:F1}, remaining={s.DistanceToWorkMm:F1} mm\nState={s.HeadState}, window={(s.Inside?"INSIDE":"WAIT")}, missed={s.Missed}, accepted={s.Accepted}";
            FifoGluePoints.Text=s.Count>0?string.Join("\n",s.GluePoints.Select((p,i)=>$"P{i+1}  X={p.X,8:F2}  Y={p.Y,8:F2} mm")):"No active product";
            if(!_benchArmed)AdsStatus.Text=$"ADS port 851 • {(s.SafeMode?"SAFE BENCH":"READ ONLY")} • error={s.ErrorCode}";
        }
        catch(Exception ex)
        {
            if(_offlineSimulationEnabled)
            {
                var s=_offlineQueue.GetSnapshot();
                _lastSnapshot=s;
                FifoCount.Text=s.Count.ToString();
                FifoProduct.Text=s.HeadProduct>0?s.HeadProduct.ToString():"—";
                FifoProgress.Text=$"VIRTUAL FIFO (PC MEMORY · ZERO ADS)\nHead: X={s.HeadXmm:F1}, Y={s.HeadYmm:F1} mm | window={(s.Inside?"INSIDE":"WAIT")}\nAccepted={s.Accepted}, Count={s.Count}/8";
                FifoGluePoints.Text=s.Count>0?string.Join("\n",s.GluePoints.Select((p,i)=>$"P{i+1}  X={p.X,8:F2}  Y={p.Y,8:F2} mm")):"No simulated product";
                if(!_benchArmed)AdsStatus.Text="OFFLINE SIMULATION ACTIVE · ZERO ADS WRITES · Virtual FIFO ready";
            }
            else
            {
                _lastSnapshot=null;
                FifoCount.Text="—";FifoProduct.Text="—";
                FifoProgress.Text="PLC not available";
                FifoGluePoints.Text="No PLC tracking";
                if(!_benchArmed)AdsStatus.Text="ADS: "+ex.Message;
            }
        }
        finally{_adsPolling=false;}
    }
    private async void SendPausedVideoFrame_Click(object sender,RoutedEventArgs e)
    {
        // Deliberately separate from START and automatic midline triggers.
        // One frozen VIDEO pose -> safety-gated TwinCAT BENCH FIFO only.
        if(_closing || !AppHardwareCapabilities.AllowAdsWrites)
        {
            AdsStatus.Text="ADS TRANSFER INHIBITED: Hardware writes are disabled in offline calibration mode.";
            return;
        }
        if(_singleBenchCts!=null){
            AdsStatus.Text="One frame is being sent; wait before trying again.";
            return;
        }
        _singleBenchCts=new CancellationTokenSource();
        var oneShotToken=_singleBenchCts.Token;
        try
        {
            if(_bridgeRunning || _benchArmed)
                throw new InvalidOperationException("Disable automatic transfers and wait for the current ADS request.");
            if(_video==null || !_paused)
                throw new InvalidOperationException("Load video, START detection and PAUSE on a frame containing a product.");
            if(!_trackingEnabled || !_last.Found)
                throw new InvalidOperationException("No valid product is detected in the paused frame.");
            if(_options.FixedMmPerPixel<=0)
                throw new InvalidOperationException("Enter a measured mm/pixel scale; do not use auto-estimated video scale for PLC transfer.");
            // Never block WPF UI with ~30 synchronous ADS symbol reads.
            // Safety is rechecked INSIDE EnqueueBenchAsync before any write.
            var state=await Task.Run(()=>_bridge.ReadStatus());
            oneShotToken.ThrowIfCancellationRequested();
            if(!state.SafeMode || state.ErrorCode!=0 || state.Count>=8
                || Math.Abs(state.EncoderMmPerSec)>0.5)
                throw new InvalidOperationException("PLC is not in safe FIFO BENCH simulation mode; encoder must be stationary and robot permits OFF.");
            var message=(
                $"Send one paused VIDEO product to TwinCAT BENCH?\nX={_lastBelt.Xmm:F2}mm, Y={_lastBelt.Ymm:F2}mm, θ={_lastBelt.ThetaDeg:F2}°\n"+
                "TwinCAT computes P1–P4; no Gantry motion command is sent.");
            if(MessageBox.Show(message,"Confirm one-shot coordinate transfer",
                MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)
                return;
            SendPausedVideoFrameButton.IsEnabled=false;
            var pose=_lastBelt;
            var reply=await _bridge.EnqueueBenchAsync(pose.Xmm,pose.Ymm,pose.ThetaDeg,oneShotToken);
            oneShotToken.ThrowIfCancellationRequested();
            var after=await Task.Run(()=>_bridge.ReadStatus());
            oneShotToken.ThrowIfCancellationRequested();
            var row=new TriggerAuditRow{
                Timestamp=DateTime.Now, Source="VIDEO_ONE_SHOT",TrackId=0,
                PreviousPx=0,CurrentPx=0,MidlinePx=0,
                Xmm=pose.Xmm,Ymm=pose.Ymm,Theta=pose.ThetaDeg,
                Delivery="PLC BENCH ACK (1 FRAME)",
                Plc=$"Accepted={after.Accepted}; Queue={after.Count}; Head={after.HeadProduct}"
            };
            _auditRows.Insert(0,row);
            if(_auditRows.Count>250)_auditRows.RemoveAt(_auditRows.Count-1);
            AppendPipelineCsv(row,"PLC_ONE_SHOT_ACK",row.Plc);
            AdsStatus.Text=reply+" | "+row.Plc;
            StatusText.Text="ADS FIFO BENCH acknowledged (no robot command).";
        }
        catch(Exception ex)
        {
            AdsStatus.Text="VIDEO → TWINCAT NOT SENT: "+ex.Message;
            StatusText.Text=AdsStatus.Text;
        }
        finally
        {
            _singleBenchCts?.Dispose();
            _singleBenchCts=null;
            if(!_closing)SendPausedVideoFrameButton.IsEnabled=true;
        }
    }

    private void BenchQueue_Click(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded)return;
        if (!AppHardwareCapabilities.AllowAdsWrites)
        {
            BenchQueueBox.IsChecked = false;
            DisarmBench();
            AdsStatus.Text = "BENCH INHIBITED: Hardware writes are disabled in offline calibration mode.";
            return;
        }
        if(BenchQueueBox.IsChecked!=true){DisarmBench();AdsStatus.Text="Bench auto queue DISARMED";return;}
        try
        {
            if(_offlineSimulationEnabled)
                throw new InvalidOperationException("Disable the virtual FIFO before testing PLC BENCH; do not run them together.");
            if(!_trackingEnabled || !HasActiveSource)
                throw new InvalidOperationException("Connect a video/camera and start detection before arming BENCH.");
            if(!_cam1.IsOpen||!_cam1.Model.IsLive(DateTime.UtcNow,TimeSpan.FromSeconds(3)))
                throw new InvalidOperationException("No valid live CAM 1 frame. BENCH denied.");
            if(_options.FixedMmPerPixel<=0)throw new InvalidOperationException("Set CALIBRATED mm/px (nonzero) first; 50mm auto estimate cannot drive FIFO");
            var st=_bridge.ReadStatus();
            if(!st.SafeMode || st.ErrorCode!=0 || st.Count>=8)
                throw new InvalidOperationException("TwinCAT must have bQueueSimMode TRUE, Robot motion permits OFF, no fault and FIFO < 8");
            if(Math.Abs(st.EncoderMmPerSec)>0.5)
                throw new InvalidOperationException("BENCH requires a stationary physical encoder in both directions; FrameID is not synchronized.");
            var answer=MessageBox.Show(
                "BENCH TEST ONLY: camera/video image crossing the 50% FOV line will send COORDINATES to PLC FIFO simulation on port 851.\n" +
                "Do NOT run conveyor/robots. FrameID, exposure and encoder are NOT synchronized!\n" +
                "Continue only when the workpiece is secured and an operator supervises. Confirm?","Enable supervised bench FIFO",MessageBoxButton.YesNo,MessageBoxImage.Warning);
            if(answer!=MessageBoxResult.Yes)throw new OperationCanceledException("Operator did not approve");
            _benchCts?.Dispose();
            _benchCts=new CancellationTokenSource();
            _benchArmed=true;
            UpdateControlSafetyBanner();
            AdsStatus.Text="BENCH ARMED: camera X/Y/θ and simulated trigger pulses only; robot motion disabled.";
        }
        catch(Exception ex){DisarmBench();AdsStatus.Text="BENCH DISARMED: "+ex.Message;}
    }
    private static double GetNumber(string raw,string label)
    {
        if(double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out double d)&&double.IsFinite(d))return d;
        if(double.TryParse(raw,NumberStyles.Float,CultureInfo.CurrentCulture,out d)&&double.IsFinite(d))return d;
        throw new ArgumentException("Invalid "+label);
    }
    private void ApplyCalibration_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            double scale=GetNumber(ScaleBox.Text,"mm/pixel");
            if(scale<0||scale>2)throw new ArgumentOutOfRangeException("mm/pixel must be 0..2");
            // Validate the COMPLETE configuration before mutating live state.
            // Previously an invalid OriginY could leave FixedMmPerPixel modified.
            double originX=OriginXBox.Text.Trim().Equals("auto",StringComparison.OrdinalIgnoreCase)
                ?double.NaN:GetNumber(OriginXBox.Text,"originX");
            double originY=OriginYBox.Text.Trim().Equals("auto",StringComparison.OrdinalIgnoreCase)
                ?double.NaN:GetNumber(OriginYBox.Text,"originY");
            double offsetX=GetNumber(OffsetXBox.Text,"offsetX");
            double offsetY=GetNumber(OffsetYBox.Text,"offsetY");
            int threshold=ThresholdPicker.SelectedIndex;
            DisarmBench();
            _trackers.Clear();
            _labelPlacements.Reset();
            _overlayDetections=Array.Empty<PlateResult>();
            PlateOverlay.Children.Clear();
            _last=PlateResult.None("Calibration changed");
            _auditByTrack.Clear();
            _options.FixedMmPerPixel=scale;
            _options.OriginPxX=originX;
            _options.OriginPxY=originY;
            _options.OffsetXmm=offsetX;
            _options.OffsetYmm=offsetY;
            _options.ThresholdMode=threshold;
            ResetDetectionHud();
            StatusText.Text="Calibration applied atomically; Track IDs reset and BENCH disarmed. Robot calibration is NOT commissioned.";
        }
        catch(Exception ex){StatusText.Text="Calibration: "+ex.Message;}
    }
    private void TestAds_Click(object sender,RoutedEventArgs e)
    {
        try{AdsStatus.Text=_ads.Check();}
        catch(Exception ex){AdsStatus.Text="ADS: "+ex.Message;}
    }
    private void WritePose_Click(object sender,RoutedEventArgs e)
    {
        // Legacy direct write bypassed the required BENCH interlocks.
        // Deliberately fail closed even when invoked via UI automation.
        EnableWriteBox.IsChecked=false;
        AdsStatus.Text="Direct ADS write is disabled. Use PLC/TwinCAT → Send one video frame in supervised BENCH mode.";
    }
    private void OpenResults_Click(object sender,RoutedEventArgs e)
    {
        string folder=Path.GetDirectoryName(_csvPath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe",folder){UseShellExecute=true});
    }
    protected override void OnClosed(EventArgs e)
    {
        _closing=true;
        _singleBenchCts?.Cancel(); // Cancel one-shot before releasing camera / UI.
        _timer.Stop();_adsTimer.Stop();_trackingEnabled=false;
        DisarmBench(); // Cancel auto BENCH handshake on window close.
        StopVideo();
        _cam1.Dispose();_cam2.Dispose();
        _cameraOwnership.Reset();
        _discovery.Dispose();
        base.OnClosed(e);
    }
}
