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
        _adsTimer.Tick+=OnAdsTick;
        _adsTimer.Start();
        InitDashboard();
        InitStationOverview();
        InitStationDashboards();
        RefreshVideos();
        ResetDetectionHud();
        RefreshCaptureMode();
        StatusText.Text="Camera offline | ADS read-only";
        if(Environment.GetCommandLineArgs().Any(a=>a.Equals("--demo",StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(()=>OpenVideo(@"C:\Users\aml\Desktop\DEMO\1.avi")));
        // QA preview only: --mock starts the conspicuously labelled TEST ONLY interlock
        // demonstration. It never writes ADS and never commands the conveyor.
        InitCameraSlots();
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
        if(_dashControlArmed)
        {
            ControlSafetyBanner.Text="HMI CONVEYOR ARMED · NO ROBOT";
            return;
        }
        // Distinguish data permission from motion authorization. A live PLC
        // state alone never confers permission to operate the robot.
        ControlSafetyBanner.Text=_benchArmed
            ?"ADS BENCH ONLY · NO MOTION"
            :"ADS READ-ONLY · NO MOTION";
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
        if(_offlineSimulationEnabled && BenchQueueBox.IsChecked==true)
        {
            BenchQueueBox.IsChecked=false;
            DisarmBench();
        }
    }
    private async void CheckLiveReadiness_Click(object sender,RoutedEventArgs e)
    {
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
            PipelineMockStatus.Text="PASS: FIFO ACK #1, four corners ±20 mm, encoder +25 mm, duplicate Track ID blocked; PC memory only.";
            StatusText.Text="SIM PASS: Camera pose → virtual FIFO → four moving points; no PLC writes.";
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
            string hostIp=HostIpBox.Text.Trim();
            string cameraIp=CameraIpBox.Text.Trim();
            StatusText.Text="Scanning HIKROBOT/Basler GigE and read-only GVCP on "+hostIp+"...";

            CameraScanResult result=await Task.Run(()=>_cam1.Scan(CameraTransportMode.Ip,cameraIp,hostIp));
            CameraPicker.ItemsSource=result.Devices;
            if(result.Devices.Count>0)
            {
                CameraPicker.SelectedIndex=0;
                if(!string.IsNullOrWhiteSpace(result.Devices[0].Address))CameraIpBox.Text=result.Devices[0].Address;
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
    private ComboBox SlotModeBox(int slot)=>slot==1?S1ModeBox:S2ModeBox;
    private ComboBox SlotDeviceBox(int slot)=>slot==1?S1DeviceBox:S2DeviceBox;
    private TextBox SlotIpBox(int slot)=>slot==1?S1CamIpBox:S2CamIpBox;
    private TextBox SlotNicBox(int slot)=>slot==1?S1HostIpBox:S2HostIpBox;
    private Button SlotScanButton(int slot)=>slot==1?S1ScanButton:S2ScanButton;
    private Button SlotConnectButton(int slot)=>slot==1?S1ConnectButton:S2ConnectButton;
    private Button SlotDisconnectButton(int slot)=>slot==1?S1DisconnectButton:S2DisconnectButton;
    private TextBlock SlotStateText(int slot)=>slot==1?S1CamLinkText:S2CamLinkText;
    private FrameworkElement SlotEmptyText(int slot)=>slot==1?S1CamEmpty:S2CamEmpty;
    private ComboBox SlotVendorBox(int slot)=>slot==1?S1VendorBox:S2VendorBox;
    private Expander SlotDetailsExpander(int slot)=>slot==1?S1DetailsExpander:S2DetailsExpander;
    private TextBlock SlotDetailsHeader(int slot)=>slot==1?S1DetailsHeader:S2DetailsHeader;
    private TextBlock SlotDetailsText(int slot)=>slot==1?S1DetailsText:S2DetailsText;
    private bool SlotVendorIsBasler(int slot)=>SlotVendorBox(slot).SelectedIndex==1;

    private static CameraTransportMode ModeOf(ComboBox box)=>
        box.SelectedIndex==1?CameraTransportMode.Usb:CameraTransportMode.Ip;

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
        SlotModeBox(slot).SelectedIndex=ModeIndex(saved.Mode);
        SlotVendorBox(slot).SelectedIndex=SlotVendorIsBasler(slot)?1:0;
        SlotIpBox(slot).Text=saved.Ip;
        SlotNicBox(slot).Text=saved.Nic;
    }

    private void SaveSlot(int slot)
    {
        var model=SlotModel(slot);
        CameraSlotSettingsStore.Save(slot,model.Mode,SlotIpBox(slot).Text.Trim(),
            SlotNicBox(slot).Text.Trim(),model.Selected?.Key??"");
    }

    private void BindSlotDevices(int slot)
    {
        var model=SlotModel(slot);
        var box=SlotDeviceBox(slot);
        box.ItemsSource=null;
        box.ItemsSource=model.FilteredDevices;
        if(model.Selected is not null)box.SelectedItem=model.Selected;
    }

    private void S1ModeChanged(object sender,SelectionChangedEventArgs e)=>SlotModeChanged(1);
    private void S2ModeChanged(object sender,SelectionChangedEventArgs e)=>SlotModeChanged(2);

    private void SlotModeChanged(int slot)
    {
        if(!_cameraUiReady||_closing)return;
        var model=SlotModel(slot);
        model.Mode=ModeOf(SlotModeBox(slot));
        model.IpText=SlotIpBox(slot).Text.Trim();
        BindSlotDevices(slot);
        UpdateCameraSlotUi(slot);
        SaveSlot(slot);
    }

    private void S1DeviceChanged(object sender,SelectionChangedEventArgs e)=>SlotDeviceChanged(1);
    private void S2DeviceChanged(object sender,SelectionChangedEventArgs e)=>SlotDeviceChanged(2);

    private void SlotDeviceChanged(int slot)
    {
        if(!_cameraUiReady||_closing)return;
        if(SlotDeviceBox(slot).SelectedItem is not CameraDevice device)return;
        SlotModel(slot).SelectDevice(device.Key);
        SaveSlot(slot);
        UpdateCameraSlotUi(slot);
    }
    private void S1IpTextChanged(object sender,TextChangedEventArgs e)
    {
        if(!_cameraUiReady||_closing)return;
        _cam1.Model.IpText=S1CamIpBox.Text.Trim();
        UpdateCameraSlotUi(1);
    }
    private void S2IpTextChanged(object sender,TextChangedEventArgs e)
    {
        if(!_cameraUiReady||_closing)return;
        _cam2.Model.IpText=S2CamIpBox.Text.Trim();
        UpdateCameraSlotUi(2);
    }

    private async void S1Scan_Click(object sender,RoutedEventArgs e)=>await ScanSlotAsync(1);
    private async void S2Scan_Click(object sender,RoutedEventArgs e)=>await ScanSlotAsync(2);

    private async Task ScanSlotAsync(int slot)
    {
        if(slot==1?_scanBusy1:_scanBusy2)return;
        if(slot==1)_scanBusy1=true;else _scanBusy2=true;
        var controller=SlotController(slot);
        var model=controller.Model;
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
            if(mode==CameraTransportMode.Ip&&CameraTransportPolicy.IsValidAddress(typedIp)&&
               !SlotVendorIsBasler(slot))
            {
                var manual=ManualHikDevice(typedIp);
                if(devices.All(d=>d.Key!=manual.Key))devices.Add(manual);
            }

            model.SetScanResult(new CameraScanResult(devices,result.Issues));
            _scannedSlots[slot]=true;
            BindSlotDevices(slot);

            if(model.Selected is null&&model.FilteredDevices.Count>0)
            {
                var first=model.FilteredDevices[0];
                if(model.SelectDevice(first.Key))
                {
                    if(mode==CameraTransportMode.Ip&&!string.IsNullOrWhiteSpace(first.Address))
                    {
                        SlotIpBox(slot).Text=first.Address;
                        model.IpText=first.Address;
                    }
                    SlotDeviceBox(slot).SelectedItem=first;
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
        var controller=SlotController(slot);
        var model=controller.Model;
        model.IpText=SlotIpBox(slot).Text.Trim();

        if(model.Mode==CameraTransportMode.Ip&&model.Selected is null&&
           CameraTransportPolicy.IsValidAddress(model.IpText))
        {
            if(SlotVendorIsBasler(slot))
            {
                StatusText.Text="BASLER typed IP is not supported: SCAN REQUIRED. Scan in IP mode and select the discovered Basler camera.";
                UpdateCameraSlotUi(slot);
                return;
            }

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
        if(!controller.Connect(model.Selected!,model.Mode,model.IpText,nic,_cameraOwnership,out error))
        {
            StatusText.Text=error;
            UpdateCameraSlotUi(slot);
            return;
        }

        _paused=false;_timer.Start();
        if(slot==1)
        {
            CameraIpBox.Text=model.IpText;
            HostIpBox.Text=nic;
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
        SlotStateText(slot).Text=state;
        SlotStateText(slot).Foreground=color;
        SlotModeBox(slot).IsEnabled=!open;
        SlotVendorBox(slot).IsEnabled=!open;
        SlotScanButton(slot).IsEnabled=!open&&(slot==1?!_scanBusy1:!_scanBusy2);
        SlotDisconnectButton(slot).IsEnabled=open;

        // CONNECT is only offered for a selected device that the policy can actually dispatch.
        bool canConnect=!open&&model.Selected is not null&&
            CameraTransportPolicy.CanDispatch(model.Selected,model.Mode,out _);
        SlotConnectButton(slot).IsEnabled=canConnect;

        bool showIp=model.Mode==CameraTransportMode.Ip;
        if(slot==1)S1IpRow.Visibility=showIp?Visibility.Visible:Visibility.Collapsed;
        else S2IpRow.Visibility=showIp?Visibility.Visible:Visibility.Collapsed;

        string hint=model.State switch
        {
            CameraSlotState.Error=>model.LastError.Length>0?model.LastError:"SCAN ERROR",
            CameraSlotState.Lost=>"NO FRAME",
            CameraSlotState.Scanning=>"SCANNING",
            CameraSlotState.Connecting=>"CONNECTING",
            CameraSlotState.Connected=>"CONNECTED · NO FRAME",
            _=>"OFFLINE · SCAN / CONNECT"
        };
        if(!open&&scannedEmpty)
        {
            if(hasIssues)hint=model.ScanIssues[0].Summary;
            else if(model.Mode==CameraTransportMode.Ip&&SlotVendorIsBasler(slot))
                hint="SCAN REQUIRED · BASLER typed IP is not supported; scan the camera in IP mode";
            else if(model.Mode==CameraTransportMode.Usb)
                hint="NO USB CAMERA · check the USB3 cable/port; Windows and pylon Viewer must list the camera";
            else
                hint="NO IP CAMERA · check camera power/link on the camera NIC and SCAN again";
        }
        if(slot==1)S1CamEmpty.Text=hint;else S2CamHint.Text=hint;
        SlotEmptyText(slot).Visibility=shownFresh?Visibility.Collapsed:Visibility.Visible;

        // Diagnostics stay hidden unless a scan recorded a vendor or GVCP failure.
        bool showDetails=hasIssues;
        SlotDetailsExpander(slot).Visibility=showDetails?Visibility.Visible:Visibility.Collapsed;
        if(showDetails)
        {
            SlotDetailsHeader(slot).Text=model.ScanIssues[0].Label;
            var details=new StringBuilder();
            foreach(var issue in model.ScanIssues)
            {
                details.Append(issue.Label).Append(" · ").Append(issue.Transport)
                       .Append(" · ").Append(issue.ErrorType).AppendLine();
                details.AppendLine(issue.Message);
                if(!string.IsNullOrWhiteSpace(issue.Details))details.AppendLine(issue.Details);
                details.AppendLine();
            }
            SlotDetailsText(slot).Text=details.ToString().Trim();
        }
        else SlotDetailsText(slot).Text="";

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
        _cam1.Model.Mode=CameraTransportMode.Ip;
        S1ModeBox.SelectedIndex=ModeIndex(CameraTransportMode.Ip);
        S1CamIpBox.Text=CameraIpBox.Text.Trim();
        S1HostIpBox.Text=HostIpBox.Text.Trim();
        ConnectSlot(1);
    }
    private void ConnectCamera_Click(object sender,RoutedEventArgs e)
    {
        if(CameraPicker.SelectedItem is CameraInfo info)Connect(CameraInfoToDevice(info));
        else StatusText.Text="Select a camera or enter an IP address.";
    }
    private void ConnectAll_Click(object sender,RoutedEventArgs e)=>ConnectCamera_Click(sender,e);
    private void Connect(CameraInfo info)=>Connect(CameraInfoToDevice(info));

    private void Connect(CameraDevice device)
    {
        var model=_cam1.Model;
        model.Mode=device.Transport.Contains("USB",StringComparison.OrdinalIgnoreCase)
            ?CameraTransportMode.Usb:CameraTransportMode.Ip;
        S1ModeBox.SelectedIndex=ModeIndex(model.Mode);
        var merged=model.Devices.Where(d=>d.Key!=device.Key).Append(device).ToList();
        model.SetDevices(merged);
        model.SelectDevice(device.Key);
        BindSlotDevices(1);
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
        var bmp=BitmapSourceConverter.ToBitmapSource(image);
        bmp.Freeze();
        Station2Preview.Source=bmp;
        Ov2CamImage.Source=bmp;
        _cam2.Model.MarkFrame(DateTime.UtcNow);
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
            HoleCoordinates.Text=_last.Found?string.Join("\n",_last.GluePx.Select((p,i)=>{
                var pos=BeltCoordinates.Map(p.X,p.Y,_last.AngleDeg,image.Width,image.Height,
                    _last.MmPerPixel,_options.OriginPxX,_options.OriginPxY,_options.OffsetXmm,_options.OffsetYmm,_beltDirection);
                return $"P{i+1}  X={pos.Xmm,8:F2}  Y={pos.Ymm,8:F2}";
            })):"No plate";
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
                var virtualAck=_offlineQueue.Accept(rowKey,pose.Xmm,pose.Ymm,pose.ThetaDeg,0);
                row.Delivery="VIRTUAL FIFO ACK #"+virtualAck.ProductId;
                row.Plc="SIMULATED 4 POINTS - NO PLC WRITE";
                PipelineMockStatus.Text=$"VIRTUAL ACK #{virtualAck.ProductId} | FIFO={virtualAck.QueueCount}/8 | 4 points: "+
                    string.Join(" ; ",virtualAck.Corners.Select((p,i)=>$"P{i+1}=({p.X:F1},{p.Y:F1})"));
                AppendPipelineCsv(row,"SIMULATED_FIFO_ACK",PipelineMockStatus.Text);
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
        if(_adsPolling)return;
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
            _lastSnapshot=null;
            FifoCount.Text="—";FifoProduct.Text="—";
            FifoProgress.Text="PLC not available";
            FifoGluePoints.Text="No PLC tracking";
            if(!_benchArmed)AdsStatus.Text="ADS: "+ex.Message;
        }
        finally{_adsPolling=false;}
    }
    private async void SendPausedVideoFrame_Click(object sender,RoutedEventArgs e)
    {
        // Deliberately separate from START and automatic midline triggers.
        // One frozen VIDEO pose -> safety-gated TwinCAT BENCH FIFO only.
        if(_closing)return;
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
