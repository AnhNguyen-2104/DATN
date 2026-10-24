using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TwinCAT.Ads;

namespace PlateVision50;

public sealed record TagValueRow(string Group,string Tag,string Value,string Quality,string Access,string Timestamp);

internal sealed class LineStatus
{
    public DateTime ReceivedUtc { get; init; }
    public IReadOnlyDictionary<string,bool> SymbolPresence { get; init; } = new Dictionary<string,bool>();
    public bool EstopOk { get; init; }
    public bool SystemEStopOk { get; init; }
    public bool ServoReady { get; init; }
    public bool GlobalStop { get; init; }
    public bool PlcQConnected { get; init; }
    public bool GantryBusy { get; init; }
    public bool GantryDone { get; init; }
    public bool GantryError { get; init; }
    public short GantryVertex { get; init; }
    public short GantryErrorCode { get; init; }
    public bool GantryPublisherEnabled { get; init; }
    public bool VisionBridgeVerified { get; init; }
    public short QueueCount { get; init; }
    public uint QueueHead { get; init; }
    public uint Accepted { get; init; }
    public uint Missed { get; init; }
    public double BeltSpeed { get; init; }
    public double BeltDistance { get; init; }
    public bool BeltRun { get; init; }
    public bool BeltRequest { get; init; }
    public double HzSet { get; init; }
    public double HzActualReference { get; init; }
    public double AoVoltsReference { get; init; }
    public ushort AoRaw { get; init; }
}

internal static class LineAds
{
    private static T Read<T>(AdsClient c, string name)
    {
        var h=c.CreateVariableHandle(name);
        try { return (T)c.ReadAny(h,typeof(T)); }
        finally { c.DeleteVariableHandle(h); }
    }
    private static void Write<T>(AdsClient c, string name, T value)
    {
        var h=c.CreateVariableHandle(name);
        try { c.WriteAny(h,value!); }
        finally { c.DeleteVariableHandle(h); }
    }
    private static T Optional<T>(AdsClient c, string name, Dictionary<string,bool> presence,T fallback=default!)
    {
        try { var value=Read<T>(c,name); presence[name]=true; return value; }
        catch { presence[name]=false; return fallback; }
    }
    public static LineStatus Poll()
    {
        using var ads=new AdsClient();
        ads.Connect(851);
        if(ads.ReadState().AdsState!=AdsState.Run)
            throw new InvalidOperationException("TwinCAT ADS 851 is not in RUN state.");
        // Global stop and conveyor IO are REQUIRED: never infer healthy on failure.
        var presence=new Dictionary<string,bool>(StringComparer.Ordinal);
        return new LineStatus {
            SymbolPresence=presence,
            ReceivedUtc=DateTime.UtcNow,
            EstopOk=Read<bool>(ads,"GVL_MD200_IO.bEStopFeedback"),
            GlobalStop=Read<bool>(ads,"MAIN.bSystemStopRequested"),
            SystemEStopOk=Optional<bool>(ads,"MAIN.bSystemEStopOK",presence),
            ServoReady=Optional<bool>(ads,"MAIN.bServo_Ready",presence),
            PlcQConnected=Optional<bool>(ads,"MAIN.bPlcQConnected",presence),
            GantryBusy=Optional<bool>(ads,"MAIN.bGlueCycleBusy",presence),
            GantryDone=Optional<bool>(ads,"MAIN.bGlueCycleDone",presence),
            GantryError=Optional<bool>(ads,"MAIN.bGlueCycleError",presence),
            GantryVertex=Optional<short>(ads,"MAIN.nGlueCurVertex",presence),
            GantryErrorCode=Optional<short>(ads,"MAIN.nGlueErrorCode",presence),
            GantryPublisherEnabled=Optional<bool>(ads,"MAIN.bLegacyMotionPublisherAllow",presence),
            VisionBridgeVerified=Optional<bool>(ads,"MAIN.bVisionFeedVerified",presence),
            QueueCount=Optional<short>(ads,"MAIN.nQueueCount",presence),
            QueueHead=Optional<uint>(ads,"MAIN.nQueueHeadProductID",presence),
            Accepted=Optional<uint>(ads,"MAIN.nQueueAccepted",presence),
            Missed=Optional<uint>(ads,"MAIN.nQueueMissed",presence),
            BeltSpeed=Optional<double>(ads,"MAIN.rConveyorSpeed",presence),
            BeltDistance=Optional<double>(ads,"MAIN.rConveyorDist",presence),
            BeltRun=Read<bool>(ads,"GVL_MD200_IO.bRUN_DO"),
            BeltRequest=Read<bool>(ads,"MAIN.bMD200_Start"),
            HzSet=Read<double>(ads,"MAIN.rMD200_SetFrequency_Hz"),
            HzActualReference=Read<double>(ads,"MAIN.rMD200_Ramped_Hz"),
            AoVoltsReference=Read<double>(ads,"MAIN.rMD200_AO_VoltsVirtual"),
            AoRaw=Read<ushort>(ads,"GVL_MD200_IO.nAO1_Raw")
        };
    }

    public static void StartBelt(double hz)
    {
        if(!double.IsFinite(hz) || hz<=0 || hz>25)
            throw new ArgumentOutOfRangeException(nameof(hz),"Frequency must be greater than 0 and no more than 25 Hz.");
        using var ads=new AdsClient();
        ads.Connect(851);
        if(ads.ReadState().AdsState!=AdsState.Run)throw new InvalidOperationException("PLC is not in RUN state.");
        if(!Read<bool>(ads,"GVL_MD200_IO.bEStopFeedback")
           || Read<bool>(ads,"MAIN.bSystemStopRequested"))
            throw new InvalidOperationException("Auxiliary E-Stop feedback is not OK. START blocked.");
        if(Read<bool>(ads,"MAIN.bGlueCycleBusy"))
            throw new InvalidOperationException("Gantry is busy. Conveyor START is blocked in this test session.");
        if(Read<bool>(ads,"MAIN.bMD200_Start"))
            throw new InvalidOperationException("Conveyor START is already requested. Stop it first.");
        // Direct HMI command ONLY for explicitly approved MD200 setpoint and start tags.
        // NEVER write EStop flags, motion publisher, M1000, QD75 or camera FIFO.
        Write(ads,"MAIN.rMD200_SetFrequency_Hz",hz);
        if(Math.Abs(Read<double>(ads,"MAIN.rMD200_SetFrequency_Hz")-hz)>0.000001)
            throw new InvalidOperationException("PLC did not acknowledge the frequency setpoint. START blocked.");
        if(!Read<bool>(ads,"GVL_MD200_IO.bEStopFeedback")
           || Read<bool>(ads,"MAIN.bSystemStopRequested"))
            throw new InvalidOperationException("E-Stop auxiliary input changed state. START cancelled.");
        try
        {
            Write(ads,"MAIN.bMD200_Start",true);
            if(!Read<bool>(ads,"MAIN.bMD200_Start"))
                throw new InvalidOperationException("PLC rejected START or E-Stop feedback is active.");
        }
        catch
        {
            // A write-ACK/readback failure can leave the physical start bit high.
            // Request STOP best-effort; operator must verify actual machine state.
            try { Write(ads,"MAIN.bMD200_Start",false); } catch { }
            throw;
        }
    }
    public static void StopBelt()
    {
        using var ads=new AdsClient();
        ads.Connect(851);
        if(ads.ReadState().AdsState!=AdsState.Run)throw new InvalidOperationException("PLC is not in RUN state.");
        Write(ads,"MAIN.bMD200_Start",false);
        if(Read<bool>(ads,"MAIN.bMD200_Start"))
            throw new InvalidOperationException("PLC has not acknowledged the normal STOP request.");
    }
}

public partial class MainWindow
{
    private readonly DispatcherTimer _dashTimer=new() {Interval=TimeSpan.FromMilliseconds(1000)};
    private bool _dashPolling;
    private bool _dashControlArmed;
    private LineStatus? _dashStatus;

    private static SolidColorBrush DashBrush(string code) =>
        new((Color)ColorConverter.ConvertFromString(code));

    private void InitDashboard()
    {
        _dashControlArmed=false;
        DashStartButton.IsEnabled=false;
        DashStopButton.IsEnabled=false;
        DashControlArm.IsChecked=false;
        _dashTimer.Tick+=DashTimer_Tick;
        _dashTimer.Start();
        Closed+=(_,_)=>_dashTimer.Stop();
        _=RefreshDashboardAsync();
    }
    private async void DashTimer_Tick(object? sender,EventArgs e) => await RefreshDashboardAsync();

    private async Task RefreshDashboardAsync()
    {
        if(_dashPolling || _closing)return;
        _dashPolling=true;
        try {
            var s=await Task.Run(LineAds.Poll);
            if(_closing)return;
            _dashStatus=s;
            UpdateCellViews(s);
            UpdateStationOverviewPlc(s);
            UpdateStationDashboards(s);
            DashUpdated.Text="ADS / "+s.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss")+
                "  |  Updated every 1 s";
            DashPlc.Text="ONLINE";
            DashPlc.Foreground=DashBrush("#71DFC4");
            DashEStop.Text=s.EstopOk && !s.GlobalStop?"DI OK (aux)":"STOP / FAULT";
            DashEStop.Foreground=DashBrush(s.EstopOk && !s.GlobalStop?"#71DFC4":"#FF978C");
            DashSpeed.Text=$"{s.BeltSpeed:F1} mm/s";
            DashFrequency.Text=$"Setpoint: {s.HzSet:F1} Hz";
            DashQueue.Text=$"{s.QueueCount} / 8";
            DashQueueInfo.Text=s.QueueCount>0?$"Product #{s.QueueHead}":"No products";
            DashBeltState.Text=s.BeltRun?"RUN OUTPUT ON":"STOP";
            DashBeltState.Foreground=DashBrush(s.BeltRun?"#71DFC4":"#EAC17E");
            DashDistance.Text=$"{s.BeltDistance:F1} mm";
            DashBeltMmS.Text=$"{s.BeltSpeed:F2} mm/s";
            DashHzReal.Text=$"{s.HzActualReference:F1} Hz";
            DashAnalog.Text=$"{s.AoVoltsReference:F2} V (raw {s.AoRaw})";
            DashGantryPlc.Text="Mitsubishi Q: "+(s.PlcQConnected?"TCP ONLINE":"DISCONNECTED");
            DashGantryState.Text=s.GantryError?"GANTRY: FAULT / NOT READY"
                :s.GantryBusy?"GANTRY: CYCLE IN PROGRESS"
                :s.GantryPublisherEnabled?"PUBLISHER ENABLED / NOT COMMISSIONED":"GANTRY: AUTO NOT COMMISSIONED";
            DashGantryVertex.Text=$"Current vertex: {s.GantryVertex} / 4 | Error: {s.GantryErrorCode}";
            DashGantryStats.Text=$"FIFO: {s.QueueCount}  |  Accepted: {s.Accepted}  |  Missed: {s.Missed}";
            DashCamera1.Text=(_directHik?.IsOpen==true||_sessions.Count>0?"Camera 1: LIVE":"Camera 1: "+(_video!=null?"VIDEO TEST":"DISCONNECTED"))
                 +(_trackingEnabled?" | DETECT ON":" | DETECT OFF");
            // Camera connection ≠ validated camera-to-PLC calibration.
            if(s.GlobalStop || !s.EstopOk)
                DashMachineMessage.Text="GLOBAL STOP REQUESTED · DEVICE START BLOCKED";
            else if(!s.PlcQConnected)
                DashMachineMessage.Text="AUX E-STOP DI OK · MITSUBISHI PLC-Q DISCONNECTED · GANTRY BLOCKED";
            else
                DashMachineMessage.Text="AUX E-STOP DI OK · TWINCAT RUN · GANTRY AUTO NOT COMMISSIONED";
            // Normal STOP is available even if command-arm switch is not selected.
            DashStopButton.IsEnabled=_operatorManualMode && _dashControlArmed;
            DashStartButton.IsEnabled=_operatorManualMode && _dashControlArmed && s.EstopOk && !s.GlobalStop
                && !s.GantryBusy && !s.BeltRequest;
        } catch(Exception ex) {
            _dashStatus=null;
            UpdateStationOverviewPlc(null);
            UpdateStationDashboards(null);
            TagMatrixTable.ItemsSource=null;
            CellConveyorQuality.Text="OFFLINE / PLC DATA UNAVAILABLE";
            CellConveyorRun.Text="MD200 RUN DO: UNKNOWN";
            CellConveyorSpeed.Text="OFFLINE / ENCODER DATA UNAVAILABLE";
            CellConveyorDistance.Text="OFFLINE / ENCODER DATA UNAVAILABLE";
            CellConveyorHz.Text="OFFLINE / MD200 DATA UNAVAILABLE";
            CellConveyorAo.Text="OFFLINE / AO DATA UNAVAILABLE";
            CvMimicBelt.Text="OFFLINE · telemetry unavailable";
            CvMimicS1.Text="Camera 1 · unknown";
            CvMimicS1State.Text="Handshake: ADS OFFLINE";
            DashSpeed.Text="-- / STALE";
            DashDistance.Text="-- / STALE";
            DashBeltMmS.Text="-- / STALE";
            DashHzReal.Text="-- / STALE";
            DashAnalog.Text="-- / STALE";
            DashQueue.Text="-- / STALE";
            DashGantryState.Text="PLC OFFLINE / GANTRY STATE UNKNOWN";
            DashUpdated.Text="ADS OFFLINE / "+DateTime.Now.ToString("HH:mm:ss");
            DashPlc.Text="DISCONNECTED";
            DashPlc.Foreground=DashBrush("#FF978C");
            DashEStop.Text="UNVERIFIED";
            DashEStop.Foreground=DashBrush("#FF978C");
            DashMachineMessage.Text="TWINCAT DATA LOST: "+ex.Message;
            DashStartButton.IsEnabled=false;
            DashStopButton.IsEnabled=false;
        } finally {_dashPolling=false;}
    }


    private void UpdateCellViews(LineStatus s)
    {
        var time=s.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss");
        string Q(string tag) => s.SymbolPresence.TryGetValue(tag,out var ok) && !ok ? "READ_FAIL" : "LIVE";
        string V(bool v) => v ? "TRUE" : "FALSE";
        // Conveyor page uses operator labels only; no terminal IDs or drive model names.
        string camera1 = _directHik?.IsOpen==true||_sessions.Count>0 ? "FRAME STREAMING" : _video!=null ? "VIDEO TEST" : "DISCONNECTED";
        CellConveyorQuality.Text=$"LIVE / {time} · read-only · one shared encoder for both stations";
        CellConveyorRun.Text=(s.BeltRun?"RUNNING":"STOPPED")+" · start request "+(s.BeltRequest?"ACTIVE":"INACTIVE")+" · camera 1 "+camera1;
        CellConveyorSpeed.Text=$"{s.BeltSpeed:F1} mm/s (measured by encoder)";
        CellConveyorDistance.Text=$"{s.BeltDistance:F1} mm since PLC reset";
        CellConveyorHz.Text=$"setpoint {s.HzSet:F1} Hz · ramped {s.HzActualReference:F1} Hz (reference, not measured shaft speed)";
        CellConveyorAo.Text=$"{s.AoVoltsReference:F2} V calculated reference (raw {s.AoRaw}) · not an electrical measurement";
        CvMimicBelt.Text=(s.BeltRun?"RUNNING":"STOPPED")+$" · {s.BeltSpeed:F1} mm/s · {s.BeltDistance:F1} mm";
        CvMimicS1.Text = camera1=="DISCONNECTED" ? "Camera 1 · no source" : "Camera 1 · "+camera1.ToLowerInvariant();
        CvMimicS1State.Text = s.GantryError ? "Handshake: GANTRY FAULT" : "Handshake: PLC-Q FIFO · "+V(s.PlcQConnected);
        TagValueRow T(string group,string tag,string val,string access="R")
            => new(group,tag,val,Q(tag),access,time);
        TagMatrixTable.ItemsSource=new[] {
            T("GLOBAL","GVL_MD200_IO.bEStopFeedback",V(s.EstopOk)),
            T("GLOBAL","MAIN.bSystemStopRequested",V(s.GlobalStop)),
            T("GLOBAL","MAIN.bSystemEStopOK",V(s.SystemEStopOk)),
            T("GLOBAL","MAIN.bServo_Ready",V(s.ServoReady)),
            T("GLOBAL","MAIN.bPlcQConnected",V(s.PlcQConnected)),
            T("CONVEYOR","MAIN.rConveyorSpeed",$"{s.BeltSpeed:F2} mm/s"),
            T("CONVEYOR","MAIN.rConveyorDist",$"{s.BeltDistance:F2} mm"),
            T("CONVEYOR","MAIN.bMD200_Start",V(s.BeltRequest),"R/W*"),
            T("CONVEYOR","MAIN.rMD200_SetFrequency_Hz",$"{s.HzSet:F2} Hz","R/W*"),
            T("CONVEYOR","MAIN.rMD200_Ramped_Hz",$"{s.HzActualReference:F2} Hz"),
            T("CONVEYOR","MAIN.rMD200_AO_VoltsVirtual",$"{s.AoVoltsReference:F2} V calc"),
            T("CONVEYOR","GVL_MD200_IO.nAO1_Raw",s.AoRaw.ToString()),
            T("CONVEYOR","GVL_MD200_IO.bRUN_DO",V(s.BeltRun)),
            T("GANTRY","MAIN.bVisionFeedVerified",V(s.VisionBridgeVerified)),
            T("GANTRY","MAIN.nQueueCount",s.QueueCount.ToString()),
            T("GANTRY","MAIN.nQueueHeadProductID",s.QueueHead.ToString()),
            T("GANTRY","MAIN.nQueueAccepted",s.Accepted.ToString()),
            T("GANTRY","MAIN.nQueueMissed",s.Missed.ToString()),
            T("GANTRY","MAIN.bLegacyMotionPublisherAllow",V(s.GantryPublisherEnabled)),
            T("GANTRY","MAIN.bGlueCycleBusy",V(s.GantryBusy)),
            T("GANTRY","MAIN.bGlueCycleDone",V(s.GantryDone)),
            T("GANTRY","MAIN.bGlueCycleError",V(s.GantryError)),
            T("GANTRY","MAIN.nGlueCurVertex",s.GantryVertex.ToString()),
            T("GANTRY","MAIN.nGlueErrorCode",s.GantryErrorCode.ToString()),
            new("SCARA","GVL_SCARA.*","--","RESERVED","--","--"),
            new("CAMERA 2","GVL_CAMERA2.*","--","RESERVED","--","--")
        };
    }

    private void DashCamera_Click(object sender,RoutedEventArgs e) => MainNavigation.SelectedItem=VisionOperationTab;
    private void DashPLC_Click(object sender,RoutedEventArgs e) =>
        MainNavigation.SelectedItem=AlarmsLogsTab;

    private void DashArmChanged(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded)return;
        if(DashControlArm.IsChecked==true)
        {
            if (!_operatorManualMode)
            {
                _dashControlArmed = false;
                DashControlArm.IsChecked = false;
                DashControlNote.Text = "Manual conveyor controls are unavailable in AUTO mode.";
                return;
            }
            var ans=MessageBox.Show(
                "Enable HMI control only after the physical emergency-stop circuit and conveyor have been commissioned.\n"+
                "START can energize the actual MD200 conveyor. This STOP is NOT an emergency stop.\n\n"+
                "Are you supervising the machine on site and authorizing conveyor control for this session?",
                "DATN / Operator Authorization",MessageBoxButton.YesNo,MessageBoxImage.Warning);
            if(ans!=MessageBoxResult.Yes){DashControlArm.IsChecked=false;return;}
            _dashControlArmed=true;
            DashControlNote.Text="CONVEYOR HMI CONTROL ARMED FOR THIS SESSION. Gantry/SCARA control remains disabled.";
        }
        else {
            _dashControlArmed=false;
            DashStartButton.IsEnabled=false;
            DashControlNote.Text="HMI controls disarmed. Normal STOP requires live ADS. Emergency stop must be hardware-wired.";
        }
        UpdateControlSafetyBanner();
    }

    private async void DashStart_Click(object sender,RoutedEventArgs e)
    {
        if(!_operatorManualMode || !_dashControlArmed || _dashStatus is null || !_dashStatus.EstopOk
            || _dashStatus.GlobalStop || _dashStatus.GantryBusy)return;
        if(!double.TryParse(DashHzInput.Text.Trim(),NumberStyles.Float,
                CultureInfo.InvariantCulture,out var hz) &&
           !double.TryParse(DashHzInput.Text.Trim(),NumberStyles.Float,
                CultureInfo.CurrentCulture,out hz))
        {MessageBox.Show("Enter a valid frequency (e.g. 5.0 Hz).");return;}
        if(hz<=0 || hz>25){MessageBox.Show("Allowed range: greater than 0 and up to 25 Hz.");return;}
        if(MessageBox.Show($"Confirm physical conveyor START at {hz:F1} Hz?\n"+
                "Check personnel, workpieces, guards and the physical E-Stop before continuing.",
                "DATN / MOTION CONFIRMATION",MessageBoxButton.YesNo,MessageBoxImage.Warning)
                !=MessageBoxResult.Yes)return;
        DashStartButton.IsEnabled=false;
        try{
            await Task.Run(()=>LineAds.StartBelt(hz));
            DashControlNote.Text=$"START request sent: {hz:F1}Hz to PLC at {DateTime.Now:HH:mm:ss}; verify actual physical feedback.";
        } catch(Exception ex) {
            DashControlNote.Text="START UNVERIFIED: "+ex.Message;
            MessageBox.Show(ex.Message,"DATN: START rejected",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        await RefreshDashboardAsync();
    }

    private async void DashStop_Click(object sender,RoutedEventArgs e)
    {
        // Fail closed: never transmit an ADS write without live telemetry and an explicit
        // operator arm for this session. STOP is the safe direction, but an unguarded write
        // to a stale/unknown PLC image is still an unverified actuation.
        if(!_operatorManualMode || _dashStatus is null||!_dashControlArmed)
        {
            DashControlNote.Text="Normal STOP blocked: "+
                (_dashStatus is null?"PLC telemetry is stale or offline.":"conveyor HMI control is not armed.")+
                " Use the PHYSICAL E-STOP in danger.";
            await RefreshDashboardAsync();
            return;
        }
        DashStopButton.IsEnabled=false;
        try{
            await Task.Run(LineAds.StopBelt);
            DashControlNote.Text=$"Normal STOP request sent at {DateTime.Now:HH:mm:ss}; visually verify the conveyor has stopped.";
        }catch(Exception ex){
            DashControlNote.Text="STOP UNVERIFIED: "+ex.Message+" · Use the PHYSICAL E-STOP in danger.";
            MessageBox.Show(ex.Message,"DATN: STOP not acknowledged",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        await RefreshDashboardAsync();
    }
}
