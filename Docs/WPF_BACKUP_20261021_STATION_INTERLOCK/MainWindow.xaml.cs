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
    private sealed record CameraSession(CameraInfo Info,UnifiedCameraService Service);
    private readonly UnifiedCameraService _discovery=new();
    private readonly List<CameraSession> _sessions=new();
    private readonly DispatcherTimer _timer=new() {Interval=TimeSpan.FromMilliseconds(75)};
    private readonly DispatcherTimer _adsTimer=new() {Interval=TimeSpan.FromMilliseconds(600)};
    private readonly VisionOptions _options=new();
    private readonly Dictionary<string,MidlineTracker> _trackers=new();
    private readonly List<string> _events=new();
    private readonly ObservableCollection<TriggerAuditRow> _auditRows=new();
    private readonly Dictionary<string,TriggerAuditRow> _auditByTrack=new();
    private readonly string _auditCsv=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlateVision50_V2","trigger_pipeline.csv");
    private readonly string _themeFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlateVision50_V2","ui_theme.txt");
    private HikDirectIpCamera? _directHik;
    private long _lastHikFrame;
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
        StatusText.Text="Select a camera and CONNECT IP for LIVE VIEW; START enables detection | ADS read-only.";
        if(Environment.GetCommandLineArgs().Any(a=>a.Equals("--demo",StringComparison.OrdinalIgnoreCase)))
            Dispatcher.BeginInvoke(new Action(()=>OpenVideo(@"C:\Users\aml\Desktop\DEMO\1.avi")));
    }
    private bool HasActiveSource => _video!=null || (_directHik?.IsOpen ?? false) || _sessions.Count>0;

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
            ControlSafetyBanner.Text="HMI: CONVEYOR CONTROL ARMED | GANTRY/SCARA CONTROL DISABLED";
            return;
        }
        // Distinguish data permission from motion authorization. A live PLC
        // state alone never confers permission to operate the robot.
        ControlSafetyBanner.Text=_benchArmed
            ?"ADS: SUPERVISED POSE BENCH ONLY | ROBOT MOTION DISABLED"
            :"ADS: READ ONLY BY DEFAULT | ROBOT MOTION DISABLED";
    }
    private void RefreshCaptureMode()
    {
        UpdateControlSafetyBanner();
        StartTrackingButton.IsEnabled=HasActiveSource && !_trackingEnabled;
        StopTrackingButton.IsEnabled=_trackingEnabled;
        string src=_directHik!=null?"CAMERA LIVE":_sessions.Count>0?"CAMERA LIVE":_video!=null?"VIDEO DEMO":"NO SOURCE";
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
            StatusText.Text=(dark?"Dark theme":"Light theme")
                +" • presentation only; ADS and robot settings unchanged.";
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
            bool frameReady=_directHik?.IsOpen==true && _directHik.FrameNumber>_lastHikFrame-1;
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

    // Navigation order: 0 LINE OVERVIEW, 1 STATION 01, 2 STATION 02, 3 SHARED CONVEYOR,
    // 4 SETTINGS, 5 ALARMS & LOGS, 6 VISION WORKSPACE, 7 CAMERA / SOURCE.
    private void OpenSettingsPage_Click(object sender,RoutedEventArgs e)
    {
        MainNavigation.SelectedItem=SettingsTab;
    }
    private void OpenMonitorPage_Click(object sender,RoutedEventArgs e)
    {
        MainNavigation.SelectedItem=VisionOperationTab;
    }
    private void OpenVisionWorkspace_Click(object sender,RoutedEventArgs e)
    {
        MainNavigation.SelectedItem=VisionOperationTab;
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
        _directHik?.Dispose();_directHik=null;_lastHikFrame=0;
        try
        {
            _video=new VideoCapture(path);
            if(!_video.IsOpened())throw new IOException("Could not read AVI file");
            _paused=false;
            _timer.Start();
            RefreshCaptureMode();
            MainNavigation.SelectedItem=VisionOperationTab;
            StatusText.Text="VIDEO PLAYBACK: "+Path.GetFileName(path)+" | press START DETECTION to begin tracking.";
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
            StatusText.Text="Scanning HIK/Basler and GVCP using host IP "+HostIpBox.Text.Trim()+"...";
            var found=new List<CameraInfo>();
            foreach(var transport in new[]{CameraTransportType.GigE,CameraTransportType.Usb})
            {
                _discovery.Enumerate(transport);
                found.AddRange(_discovery.CameraList.Select(v=>new CameraInfo
                {
                    BackendIndex=v.BackendIndex,Transport=v.Transport,Vendor=v.Vendor,
                    Name=v.Name,SerialNumber=v.SerialNumber,Index=found.Count
                }));
            }
            IReadOnlyList<string> ipResults=Array.Empty<string>();
            string gvcpError="";
            try {
                ipResults=await GvcpDiscovery.ScanAsync(HostIpBox.Text.Trim(),950);
            } catch(Exception ex) {gvcpError=ex.Message;}
            foreach(var ip in ipResults)
            {
                // Manual-IP device info is sentinel BackendIndex=-1, never passed to normal enumeration.
                if(!found.Any(c=>c.Name.Contains(ip,StringComparison.OrdinalIgnoreCase)))
                    found.Add(new CameraInfo{Vendor=CameraVendor.Hikvision,Transport=CameraTransportType.GigE,
                        BackendIndex=-1,Index=found.Count,SerialNumber=ip,
                        Name="HIKROBOT GigE (IP "+ip+")"});
            }
            CameraPicker.ItemsSource=found
                .GroupBy(c=>c.Vendor+"|"+c.Transport+"|"+c.SerialNumber+"|"+c.BackendIndex)
                .Select(g=>g.First()).ToList();
            if(CameraPicker.Items.Count>0)CameraPicker.SelectedIndex=0;
            if(ipResults.Count>0)CameraIpBox.Text=ipResults[0];
            StatusText.Text=$"Camera: {CameraPicker.Items.Count} cameras; GVCP {ipResults.Count} IP"+(gvcpError==""?"":" | GVCP: "+gvcpError);
        }
        catch(Exception ex){StatusText.Text="Scan error: "+ex.Message;}
    }
    private void ConnectCameraIp_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            string cameraIp=CameraIpBox.Text.Trim(), localIp=HostIpBox.Text.Trim();
            if(!System.Net.IPAddress.TryParse(cameraIp,out var camAddr)
              ||camAddr.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork
              ||!System.Net.IPAddress.TryParse(localIp,out var hostAddr)
              ||hostAddr.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Enter valid IPv4 addresses for the camera and host Ethernet adapter.");
            bool localAssigned=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n=>n.GetIPProperties().UnicastAddresses)
                .Any(a=>a.Address.Equals(hostAddr));
            if(!localAssigned)throw new ArgumentException("Local IP "+localIp+" is not configured on any local network adapter. Do not change IP settings automatically.");
            StopTrackingForSourceChange();
            _directHik?.Dispose(); _directHik=null;
            var next=new HikDirectIpCamera(cameraIp,localIp);
            try {next.Open();} catch {next.Dispose();throw;}
            foreach(var session in _sessions){try{session.Service.Dispose();}catch{}}
            _sessions.Clear();
            _directHik=next; _lastHikFrame=0;
            StopVideo(); _paused=false; _timer.Start();
            ResetDetectionHud();RefreshCaptureMode();
            MainNavigation.SelectedItem=VisionOperationTab;
            StatusText.Text="Connected to HIKROBOT "+cameraIp+" — LIVE VIEW. Press START DETECTION.";
        }
        catch(Exception ex) {RefreshCaptureMode();StatusText.Text="IP connection failed: "+ex.Message;}
    }

    private void ConnectCamera_Click(object sender,RoutedEventArgs e)
    {
        if(CameraPicker.SelectedItem is CameraInfo info)
        {
            if(info.BackendIndex<0) {CameraIpBox.Text=info.SerialNumber;ConnectCameraIp_Click(sender,e);}
            else Connect(info);
        }
        else StatusText.Text="Select a camera or enter an IP address.";
    }
    private void ConnectAll_Click(object sender,RoutedEventArgs e)
    {
        foreach(CameraInfo item in CameraPicker.Items)
        {
            if(item.BackendIndex<0){CameraIpBox.Text=item.SerialNumber;ConnectCameraIp_Click(sender,e);}
            else Connect(item);
        }
    }
    private void Connect(CameraInfo info)
    {
        if(_sessions.Any(s=>Same(s.Info,info)))return;
        StopTrackingForSourceChange();
        _directHik?.Dispose();_directHik=null;_lastHikFrame=0;
        UnifiedCameraService? camera=null;
        try
        {
            camera=new UnifiedCameraService();
            camera.Enumerate(info.Transport);
            if(!camera.Open(info))throw new Exception("Open refused by camera SDK");
            camera.Start();
            _sessions.Add(new(info,camera));
            StopVideo();_paused=false;_timer.Start();
            ResetDetectionHud();RefreshCaptureMode();
            MainNavigation.SelectedItem=VisionOperationTab;
            StatusText.Text="Connected: "+info.Name+" — LIVE VIEW. Press START DETECTION.";
        }
        catch(Exception ex){camera?.Dispose();RefreshCaptureMode();StatusText.Text="Camera: "+ex.Message;}
    }
    private static bool Same(CameraInfo a,CameraInfo b)=>a.Vendor==b.Vendor&&a.Transport==b.Transport
        &&a.SerialNumber==b.SerialNumber&&a.BackendIndex==b.BackendIndex;
    private void DisconnectCameras_Click(object sender,RoutedEventArgs e)
    {
        StopTrackingForSourceChange();
        foreach(var s in _sessions){try{s.Service.Dispose();}catch{}}
        _sessions.Clear();
        _directHik?.Dispose();_directHik=null;_lastHikFrame=0;
        if(_video==null)_timer.Stop();
        _paused=false;
        RefreshCaptureMode();
        if(_video==null){Preview.Source=null;FrameInfo.Text="Camera disconnected";}
        StatusText.Text="Camera disconnected; tracking stopped.";
    }
    private void StopVideo()
    {
        _video?.Dispose();_video=null;
        if(_sessions.Count==0 && _directHik==null)_timer.Stop();
    }
    private void OnTick(object? sender,EventArgs e)
    {
        if(_paused && _video!=null)return;
        try
        {
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
            else if(_directHik!=null)
            {
                long frameId=_directHik.FrameNumber;
                if(frameId>_lastHikFrame)
                {
                    using var image=_directHik.CloneLatestFrame();
                    if(image!=null&&!image.Empty())
                    {
                        _lastHikFrame=frameId;
                        string frameLabel="HIK IP "+_directHik.CameraIp+" #"+frameId;
                        if(_trackingEnabled)ProcessFrame(image,frameLabel,true,"HIK/"+_directHik.CameraIp);
                        else PreviewOnly(image,frameLabel);
                    }
                }
                else if(!string.IsNullOrEmpty(_directHik.LastError))StatusText.Text=_directHik.LastError;
            }
            else if(_sessions.Count>0)
            {
                var chosen=CameraPicker.SelectedItem as CameraInfo;
                foreach(var session in _sessions)
                {
                    using var frame=session.Service.CloneLatestFrame();
                    if(frame==null||frame.Empty())continue;
                    bool selected=chosen==null?ReferenceEquals(session,_sessions[0]):Same(session.Info,chosen);
                    string sourceKey=session.Info.Vendor+"/"+session.Info.Transport+"/"+session.Info.SerialNumber+"/"+session.Info.BackendIndex;
                    string frameLabel=session.Info.Name+" frame "+(++_frameNumber);
                    if(_trackingEnabled)ProcessFrame(frame,frameLabel,selected,sourceKey);
                    else if(selected)PreviewOnly(frame,frameLabel);
                }
            }
        }
        catch(Exception ex){StatusText.Text="Frame error: "+ex.Message;}
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
            return;
        }
        VisionLabelOverlay.Render(PlateOverlay,_overlayDetections,_overlayTrackIds,_options,
            _beltDirection,_overlayImageWidth,_overlayImageHeight,
            PlateOverlay.ActualWidth,PlateOverlay.ActualHeight,
            _labelPlacements,_overlaySourceKey,_lineFraction);
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
            if(_directHik!=null && _lastHikFrame<=0)
                throw new InvalidOperationException("No valid live Hikrobot camera frame. BENCH denied.");
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
        _directHik?.Dispose();_directHik=null;
        foreach(var s in _sessions){try{s.Service.Dispose();}catch{}}
        _discovery.Dispose();
        base.OnClosed(e);
    }
}
