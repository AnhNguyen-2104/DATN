using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TwinCAT.Ads;
using System.Linq;
using PlateVision50.Vision;

namespace PlateVision50;

public sealed record TagValueRow(string Group,string Tag,string Value,string Quality,string Access,string Timestamp);

internal sealed class LineStatus
{
    public DateTime ReceivedUtc { get; init; }
    public IReadOnlyDictionary<string,bool> SymbolPresence { get; init; } = new Dictionary<string,bool>();
    public bool EstopOk { get; init; }
    public bool SystemEStopOk { get; init; }
    public bool GlobalStop { get; init; }
    public bool PlcQConnected { get; init; }
    public bool PlcQError { get; init; }
    public uint PlcQErrorId { get; init; }
    public bool GantryBusy { get; init; }
    public bool GantryDone { get; init; }
    public bool GantryError { get; init; }
    public short GantryVertex { get; init; }
    public short GantryErrorCode { get; init; }
    public uint GantryCommErrorId { get; init; }
    public bool StopCommandConfirmed { get; init; }
    public short AutoDispatchState { get; init; }
    public bool GantryPublisherEnabled { get; init; }
    public bool VisionBridgeVerified { get; init; }
    public short QueueCount { get; init; }
    public uint QueueHead { get; init; }
    public uint QueueRealCompleted { get; init; }
    public double QueueHeadX_mm { get; init; }
    public double QueueHeadY_mm { get; init; }
    public double GantryTargetX_mm { get; init; }
    public double GantryTargetY_mm { get; init; }
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
    public bool HmiLinkFresh { get; init; }
    public bool BeltSpeedMapReady { get; init; }
    public ushort BeltSpeedMapState { get; init; }
    public ushort BeltCommandResult { get; init; }
    public uint BeltAckSequence { get; init; }
    public ushort LineCommandResult { get; init; }
    public uint LineAckSequence { get; init; }
    public bool LineAutoAccepted { get; init; }
    public bool LineNormalStopLatched { get; init; }
    public double RequestedSpeedMmS { get; init; }
    public double AppliedSpeedMmS { get; init; }
    public double MeasuredMmPerHz { get; init; }
    public bool MailboxSymbolsAvailable =>
        LineMailboxProtocol.AllReadbacksPresent(SymbolPresence);
}

internal static class LineAds
{
    private static T Read<T>(AdsClient c, string name)
    {
        var h=c.CreateVariableHandle(name);
        try { return (T)c.ReadAny(h,typeof(T)); }
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
            EstopOk=Optional<bool>(ads,"GVL_MD200_IO.bEStopFeedback",presence,false),
            GlobalStop=Optional<bool>(ads,"MAIN.bSystemStopRequested",presence,true),
            SystemEStopOk=Optional<bool>(ads,"MAIN.bSystemEStopOK",presence),
            PlcQConnected=Optional<bool>(ads,"MAIN.bPlcQConnected",presence),
            PlcQError=Optional<bool>(ads,"MAIN.bPlcQError",presence,false),
            PlcQErrorId=Optional<uint>(ads,"MAIN.nPlcQErrorID",presence,0u),
            GantryBusy=Optional<bool>(ads,"MAIN.bGlueCycleBusy",presence),
            GantryDone=Optional<bool>(ads,"MAIN.bGlueCycleDone",presence),
            GantryError=Optional<bool>(ads,"MAIN.bGlueCycleError",presence),
            GantryVertex=Optional<short>(ads,"MAIN.nGlueCurVertex",presence),
            GantryErrorCode=Optional<short>(ads,"MAIN.nGlueErrorCode",presence),
            GantryCommErrorId=Optional<uint>(ads,"MAIN.nGlueCommErrorID",presence,0u),
            StopCommandConfirmed=Optional<bool>(ads,"MAIN.bStopCommandConfirmed",presence,false),
            AutoDispatchState=Optional<short>(ads,"MAIN.nAutoDispatchState",presence,(short)0),
            GantryPublisherEnabled=Optional<bool>(ads,"MAIN.bLegacyMotionPublisherAllow",presence),
            VisionBridgeVerified=Optional<bool>(ads,"MAIN.bVisionFeedVerified",presence),
            QueueCount=Optional<short>(ads,"MAIN.nQueueCount",presence),
            QueueHead=Optional<uint>(ads,"MAIN.nQueueHeadProductID",presence),
            QueueRealCompleted=Optional<uint>(ads,"MAIN.nQueueRealCompleted",presence,0u),
            QueueHeadX_mm=Optional<double>(ads,"MAIN.rQueueHeadX_mm",presence,0.0),
            QueueHeadY_mm=Optional<double>(ads,"MAIN.rQueueHeadY_mm",presence,0.0),
            GantryTargetX_mm=Optional<double>(ads,"MAIN.fbPublisher.rTargetX_mm",presence,0.0),
            GantryTargetY_mm=Optional<double>(ads,"MAIN.fbPublisher.rTargetY_mm",presence,0.0),
            Accepted=Optional<uint>(ads,"MAIN.nQueueAccepted",presence),
            Missed=Optional<uint>(ads,"MAIN.nQueueMissed",presence),
            BeltSpeed=Optional<double>(ads,"MAIN.rConveyorSpeed",presence),
            BeltDistance=Optional<double>(ads,"MAIN.rConveyorDist",presence),
            BeltRun=Optional<bool>(ads,"GVL_MD200_IO.bRUN_DO",presence,false),
            BeltRequest=Optional<bool>(ads,"MAIN.bMD200_Start",presence,false),
            HzSet=Optional<double>(ads,"MAIN.rMD200_SetFrequency_Hz",presence,0.0),
            HzActualReference=Optional<double>(ads,"MAIN.rMD200_Ramped_Hz",presence,0.0),
            AoVoltsReference=Optional<double>(ads,"MAIN.rMD200_AO_VoltsVirtual",presence,0.0),
            AoRaw=Optional<ushort>(ads,"GVL_MD200_IO.nAO1_Raw",presence,(ushort)0),
            HmiLinkFresh=Optional<bool>(ads,"MAIN.bHmiLinkFresh",presence),
            BeltSpeedMapReady=Optional<bool>(ads,"MAIN.bBeltSpeedMapReady",presence),
            BeltSpeedMapState=Optional<ushort>(ads,"MAIN.nBeltSpeedMapState",presence),
            BeltCommandResult=Optional<ushort>(ads,"MAIN.nBeltCommandResult",presence),
            BeltAckSequence=Optional<uint>(ads,"MAIN.udiBeltLastAckSeq",presence),
            LineCommandResult=Optional<ushort>(ads,"MAIN.nLineCommandResult",presence),
            LineAckSequence=Optional<uint>(ads,"MAIN.udiLineLastAckSeq",presence),
            LineAutoAccepted=Optional<bool>(ads,"MAIN.bLineAutoAccepted",presence),
            LineNormalStopLatched=Optional<bool>(ads,"MAIN.bLineNormalStopLatched",presence,false),
            RequestedSpeedMmS=Optional<double>(ads,"MAIN.rMD200_SetSpeed_mm_s",presence,0.0),
            AppliedSpeedMmS=Optional<double>(ads,"MAIN.rBeltAppliedSpeed_mm_s",presence,0.0),
            MeasuredMmPerHz=Optional<double>(ads,"MAIN.rBeltMeasuredMmPerHz",presence)
        };
    }

    // Legacy direct-Hz writes intentionally retired. New command support must use
    // sequenced TwinCAT mailbox after independent commissioning, never this class.

}

public partial class MainWindow
{
    private readonly DispatcherTimer _dashTimer=new() {Interval=TimeSpan.FromMilliseconds(200)};
    private readonly DispatcherTimer _heartbeatTimer=new() {Interval=TimeSpan.FromMilliseconds(300)};
    private bool _dashPolling;
    private bool _dashControlArmed;
    private LineStatus? _dashStatus;
    // Station interlock + one shared speed preset. No ADS write path is added here:
    // the AUTO mailbox is not present in the running PLC image until commissioning.
    private readonly LineInterlockModel _lineInterlock = new();
    private double _presetSpeedMmS = 100.0;
    private const bool LineAutoContractCommissioned = false;

    private static SolidColorBrush DashBrush(string code) =>
        new((Color)ColorConverter.ConvertFromString(code));

    private void InitDashboard()
    {
        _dashControlArmed=false;
        DashStartButton.IsEnabled=false;
        DashStopButton.IsEnabled=false;
        DashControlArm.IsChecked=false;
        DashControlArm.IsEnabled=true;
        DashControlNote.Text="MANUAL CONVEYOR COMMANDS FAIL-CLOSED. Central mm/s preset; commands await PLC/HMI commissioning.";
        PopulateOfflineTagMatrix();
        if (AppHardwareCapabilities.AllowAdsPolling)
        {
            _dashTimer.Tick+=DashTimer_Tick;
            _dashTimer.Start();
            _heartbeatTimer.Tick+=HeartbeatTimer_Tick;
            _heartbeatTimer.Start();
        }
        else
        {
            LineMailboxGateway.Instance.DisconnectTransport();
            DashPlc.Text="OFFLINE (NO HARDWARE)";
            DashPlc.Foreground=DashBrush("#92AABE");
            DashEStop.Text="DISARMED";
            DashEStop.Foreground=DashBrush("#92AABE");
            DashUpdated.Text="OFFLINE CALIBRATION / ZERO ADS";
            DashControlNote.Text="OFFLINE CALIBRATION MODE: ADS polling, transport and heartbeats strictly inhibited.";
        }
        Closed+=(_,_)=>{
            _dashTimer.Stop();
            _heartbeatTimer.Stop();
            LineMailboxGateway.Instance.DisconnectTransport();
        };
    }
    private void HeartbeatTimer_Tick(object? sender,EventArgs e)
    {
        if(_closing || !AppHardwareCapabilities.AllowAdsWrites)return;
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        LineMailboxGateway.Instance.TickHeartbeat(false, adsOnline);
    }
    private async void DashTimer_Tick(object? sender,EventArgs e) => await RefreshDashboardAsync();

    private async Task RefreshDashboardAsync()
    {
        if(_dashPolling || _closing || !AppHardwareCapabilities.AllowAdsPolling)return;
        _dashPolling=true;
        try {
            var s=await Task.Run(LineAds.Poll);
            if(_closing)return;
            _dashStatus=s;
            LineMailboxGateway.Instance.EnsureTransportConnected(851);
            UpdateCellViews(s);
            UpdateStationOverviewPlc(s);
            UpdateStationDashboards(s);
            RefreshInterlockUi();
            DashUpdated.Text="ADS / "+s.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss")+
                "  |  Updated every 200 ms";
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
            DashGantryPlc.Text = s.PlcQError ? $"Mitsubishi Q: MC ERROR ({s.PlcQErrorId})" : ("Mitsubishi Q: " + (s.PlcQConnected ? "TCP ONLINE" : "DISCONNECTED"));
            DashGantryState.Text = s.GantryError ? $"GANTRY: FAULT (CODE {s.GantryErrorCode})"
                : s.GantryBusy ? "GANTRY: CYCLE IN PROGRESS"
                : s.StopCommandConfirmed ? "GANTRY: STOP CONFIRMED"
                : s.GantryPublisherEnabled ? "PUBLISHER ENABLED / NOT COMMISSIONED" : "GANTRY: AUTO NOT COMMISSIONED";
            DashGantryVertex.Text = $"Current vertex: {s.GantryVertex} / 4 | State: {s.AutoDispatchState}";
            DashGantryStats.Text = $"FIFO: {s.QueueCount}  |  Acc: {s.Accepted}  |  Done: {s.QueueRealCompleted}  |  Miss: {s.Missed}";
            DashCamera1.Text=(_cam1.IsOpen?"Camera 1: LIVE":"Camera 1: "+(_video!=null?"VIDEO TEST":"DISCONNECTED"))
                 +(_trackingEnabled?" | DETECT ON":" | DETECT OFF");
            // Camera connection ≠ validated camera-to-PLC calibration.
            if(s.GlobalStop || !s.EstopOk)
                DashMachineMessage.Text="GLOBAL STOP REQUESTED · DEVICE START BLOCKED";
            else if(!s.PlcQConnected)
                DashMachineMessage.Text="AUX E-STOP DI OK · MITSUBISHI PLC-Q DISCONNECTED · GANTRY BLOCKED";
            else
                DashMachineMessage.Text="AUX E-STOP DI OK · TWINCAT RUN · GANTRY AUTO NOT COMMISSIONED";
            bool isRunning = s.BeltRun;
            if (!_conveyorCalibrationMode)
            {
                DashControlArm.IsEnabled = false;
                DashControlArm.IsChecked = false;
                _dashControlArmed = false;
                DashStartButton.IsEnabled = false;
                DashStopButton.IsEnabled = isRunning; // CRIT-03: keep STOP enabled while running!
                if (ConveyorManualControlPanel != null)
                    ConveyorManualControlPanel.IsEnabled = false;
                if (ConveyorManualModeNote != null)
                    ConveyorManualModeNote.Text = "CALIBRATION DISARMED: Switch to 'CONVEYOR SUBSYSTEM CALIBRATION' on LINE OVERVIEW to enable calibration controls.";

                if (isRunning)
                {
                    DashControlNote.Text = $"CONVEYOR RUNNING at {s.BeltSpeed:F1} mm/s · MANUAL RUN ACTIVE · NOT PRODUCTION AUTO. In danger use PHYSICAL E-STOP.";
                }
                else
                {
                    string reason = LineMailboxProtocol.BlockReason(
                        LineMailboxProtocol.IsTelemetryFresh(s.ReceivedUtc,DateTime.UtcNow,TimeSpan.FromSeconds(3)),
                        s.MailboxSymbolsAvailable,s.EstopOk,s.GlobalStop,
                        s.BeltSpeedMapReady,s.HmiLinkFresh);
                    string extra = "";
                    if (s.LineNormalStopLatched) extra += " | NORMAL STOP LATCHED";
                    if (!s.BeltSpeedMapReady) extra += " | BELT NOT COMMISSIONED";
                    if (!LineMailboxProtocol.ProductionCommandSourceApproved)
                    {
                        bool writeConnected = LineMailboxGateway.Instance.Transport.IsConnected;
                        extra += writeConnected ? " | WRITE TRANSPORT: CONNECTED · DISARMED" : " | WRITE TRANSPORT: DISCONNECTED · DISARMED";
                    }
                    else
                    {
                        bool writeConnected = LineMailboxGateway.Instance.Transport.IsConnected;
                        extra += writeConnected ? " | WRITE TRANSPORT: LIVE · COMMISSIONED" : " | WRITE TRANSPORT: RECONNECTING";
                    }
                    DashControlNote.Text = "CONVEYOR CONTROLS INACTIVE: Select 'CONVEYOR SUBSYSTEM CALIBRATION' on LINE OVERVIEW to enable conveyor running. " + reason + extra;
                }
            }
            else
            {
                if (ConveyorManualControlPanel != null)
                    ConveyorManualControlPanel.IsEnabled = true;
                if (ConveyorManualModeNote != null)
                    ConveyorManualModeNote.Text = "CONVEYOR SUBSYSTEM ISOLATION: Station 01 Gantry and Station 02 SCARA are disarmed during conveyor calibration. Camera 1 is not required. Production AUTO line requires verified camera and separate commissioning on Overview.";

                DashControlArm.IsEnabled = true;
                bool adsFresh = LineMailboxProtocol.IsTelemetryFresh(s.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
                bool speedOk = !double.IsNaN(_presetSpeedMmS) && _presetSpeedMmS >= LineSpeedPreset.MinMmS && _presetSpeedMmS <= LineSpeedPreset.MaxMmS;
                bool canStart = _dashControlArmed && adsFresh && s.MailboxSymbolsAvailable && LineMailboxProtocol.ProductionCommandSourceApproved
                    && s.EstopOk && !s.GlobalStop && s.HmiLinkFresh && !s.LineNormalStopLatched && s.BeltSpeedMapReady && speedOk && !isRunning;

                DashStartButton.IsEnabled = canStart;
                DashStopButton.IsEnabled = _dashControlArmed || isRunning;
                string? blockedReason = null;
                if (!adsFresh)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: ADS 851 offline or stale telemetry.";
                else if (!s.MailboxSymbolsAvailable)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: HMI PLC mailbox symbols missing on runtime.";
                else if (!LineMailboxProtocol.ProductionCommandSourceApproved)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: Production command source is not approved.";
                else if (!s.EstopOk)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: Emergency stop auxiliary contact (ST-1228 DI0) not healthy. In danger use PHYSICAL E-STOP.";
                else if (s.GlobalStop && !s.LineNormalStopLatched)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: Global stop active in PLC. In danger use PHYSICAL E-STOP.";
                else if (!s.HmiLinkFresh)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: HMI watchdog heartbeat stale in PLC.";
                else if (s.LineNormalStopLatched)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: Normal stop latched in PLC; reset required.";
                else if (!s.BeltSpeedMapReady)
                    blockedReason = "CONVEYOR CALIBRATION BLOCKED: Belt speed map not ready in PLC.";
                else if (!speedOk)
                    blockedReason = $"CONVEYOR CALIBRATION BLOCKED: Speed {_presetSpeedMmS:F1} mm/s outside [1.0..150.0].";

                if (isRunning)
                    DashControlNote.Text = $"CONVEYOR CALIBRATION RUNNING at {s.BeltSpeed:F1} mm/s · ISOLATED BELT RUN (NO ROBOT/GLUE) · In danger use PHYSICAL E-STOP.";
                else if (blockedReason != null)
                    DashControlNote.Text = blockedReason;
                else if (_dashControlArmed)
                    DashControlNote.Text = $"CONVEYOR CALIBRATION ARMED at {_presetSpeedMmS:F1} mm/s · Ready to command conveyor · ISOLATED BELT RUN (NO ROBOT/GLUE).";
                else
                    DashControlNote.Text = "CONVEYOR CALIBRATION READY (DISARMED): Check arm checkbox to enable start · ISOLATED BELT RUN (NO ROBOT/GLUE) · In danger use PHYSICAL E-STOP.";
            }
        } catch(Exception ex) {
            _dashStatus=null;
            LineMailboxGateway.Instance.DisconnectTransport();
            UpdateStationOverviewPlc(null);
            PopulateOfflineTagMatrix();
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
            RefreshInterlockUi();
            UpdateControlSafetyBanner();
        } finally {_dashPolling=false;}
    }


    private void UpdateCellViews(LineStatus s)
    {
        var time=s.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss");
        string Q(string tag) => LineMailboxProtocol.ClassifyQuality(tag, s.SymbolPresence.TryGetValue(tag,out var ok) && ok);
        string V(bool v) => v ? "TRUE" : "FALSE";
        // Conveyor page uses operator labels only; no terminal IDs or drive model names.
        string camera1 = _cam1.IsOpen ? "FRAME STREAMING" : _video!=null ? "VIDEO TEST" : "DISCONNECTED";
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
            T("GLOBAL","MAIN.bPlcQConnected",V(s.PlcQConnected)),
            T("CONVEYOR","MAIN.rConveyorSpeed",$"{s.BeltSpeed:F2} mm/s"),
            T("CONVEYOR","MAIN.rConveyorDist",$"{s.BeltDistance:F2} mm"),
            T("CONVEYOR","MAIN.bMD200_Start",V(s.BeltRequest)),
            T("CONVEYOR","MAIN.rMD200_SetFrequency_Hz",$"{s.HzSet:F2} Hz derived"),
            T("CONVEYOR","MAIN.rMD200_Ramped_Hz",$"{s.HzActualReference:F2} Hz"),
            T("CONVEYOR","MAIN.rMD200_SetSpeed_mm_s",$"{s.RequestedSpeedMmS:F2} mm/s requested"),
            T("CONVEYOR","MAIN.rBeltAppliedSpeed_mm_s",$"{s.AppliedSpeedMmS:F2} mm/s accepted"),
            T("CONVEYOR","MAIN.bBeltSpeedMapReady",V(s.BeltSpeedMapReady)),
            T("CONVEYOR","MAIN.nBeltSpeedMapState",LineMailboxProtocol.DescribeSpeedMap(s.BeltSpeedMapState)),
            T("CONVEYOR","MAIN.nBeltCommandResult",s.BeltCommandResult.ToString()),
            T("CONVEYOR","MAIN.udiBeltLastAckSeq",s.BeltAckSequence.ToString()),
            T("HMI","MAIN.bHmiLinkFresh",V(s.HmiLinkFresh)),
            T("HMI","MAIN.nLineCommandResult",s.LineCommandResult.ToString()),
            T("HMI","MAIN.udiLineLastAckSeq",s.LineAckSequence.ToString()),
            T("HMI","MAIN.bLineAutoAccepted",V(s.LineAutoAccepted)),
            T("HMI","MAIN.bLineNormalStopLatched",V(s.LineNormalStopLatched)),
            T("HMI","MAIN.bHmiHeartbeat",V(LineMailboxGateway.Instance.CurrentHeartbeat),"W"),
            T("HMI","MAIN.bHmiLineStartReq","FALSE (disarmed)","W"),
            T("HMI","MAIN.bHmiLineStopReq","FALSE (disarmed)","W"),
            T("HMI","MAIN.bHmiLineResetReq","FALSE (disarmed)","W"),
            T("HMI","MAIN.udiHmiLineCmdSeq",LineMailboxGateway.Instance.CurrentSequence.ToString(),"W"),
            T("HMI","MAIN.udiHmiBeltCmdSeq",LineMailboxGateway.Instance.CurrentManualSequence.ToString(),"W"),
            T("HMI","MAIN.bHmiBeltSetReq","FALSE (disarmed)","W"),
            T("HMI","MAIN.bHmiBeltStartReq","FALSE (disarmed)","W"),
            T("HMI","MAIN.bHmiBeltStopReq","FALSE (disarmed)","W"),
            T("HMI","MAIN.rHmiBeltSetSpeed_mm_s",$"{_presetSpeedMmS:F1} mm/s","W"),
            T("HMI","MAIN.rBeltMeasuredMmPerHz",$"{s.MeasuredMmPerHz:F4} mm/s/Hz"),
            T("HMI","MAIN.rBeltCalculatedSet_Hz",$"{s.HzSet:F2} Hz derived"),
            T("CONVEYOR","MAIN.rMD200_AO_VoltsVirtual",$"{s.AoVoltsReference:F2} V calc"),
            T("CONVEYOR","GVL_MD200_IO.nAO1_Raw",s.AoRaw.ToString()),
            T("CONVEYOR","GVL_MD200_IO.bRUN_DO",V(s.BeltRun)),
            T("GANTRY","MAIN.bVisionFeedVerified",V(s.VisionBridgeVerified)),
            T("GANTRY","MAIN.bPlcQConnected",V(s.PlcQConnected)),
            T("GANTRY","MAIN.bPlcQError",V(s.PlcQError)),
            T("GANTRY","MAIN.nPlcQErrorID",s.PlcQErrorId.ToString()),
            T("GANTRY","MAIN.nQueueCount",s.QueueCount.ToString()),
            T("GANTRY","MAIN.nQueueHeadProductID",s.QueueHead.ToString()),
            T("GANTRY","MAIN.nQueueAccepted",s.Accepted.ToString()),
            T("GANTRY","MAIN.nQueueMissed",s.Missed.ToString()),
            T("GANTRY","MAIN.nQueueRealCompleted",s.QueueRealCompleted.ToString()),
            T("GANTRY","MAIN.rQueueHeadX_mm",$"{s.QueueHeadX_mm:F1} mm"),
            T("GANTRY","MAIN.rQueueHeadY_mm",$"{s.QueueHeadY_mm:F1} mm"),
            T("GANTRY","MAIN.fbPublisher.rTargetX_mm",$"{s.GantryTargetX_mm:F1} mm"),
            T("GANTRY","MAIN.fbPublisher.rTargetY_mm",$"{s.GantryTargetY_mm:F1} mm"),
            T("GANTRY","MAIN.bLegacyMotionPublisherAllow",V(s.GantryPublisherEnabled)),
            T("GANTRY","MAIN.bGlueCycleBusy",V(s.GantryBusy)),
            T("GANTRY","MAIN.bGlueCycleDone",V(s.GantryDone)),
            T("GANTRY","MAIN.bGlueCycleError",V(s.GantryError)),
            T("GANTRY","MAIN.nGlueCurVertex",s.GantryVertex.ToString()),
            T("GANTRY","MAIN.nGlueErrorCode",s.GantryErrorCode.ToString()),
            T("GANTRY","MAIN.nGlueCommErrorID",s.GantryCommErrorId.ToString()),
            T("GANTRY","MAIN.bStopCommandConfirmed",V(s.StopCommandConfirmed)),
            T("GANTRY","MAIN.nAutoDispatchState",s.AutoDispatchState.ToString()),
            new("SCARA","GVL_SCARA.*","--","RESERVED","--","--"),
            new("CAMERA 2","GVL_CAMERA2.*","--","RESERVED","--","--")
        };
    }

    private void PopulateOfflineTagMatrix()
    {
        var time = DateTime.Now.ToString("HH:mm:ss");
        TagValueRow T(string group, string tag, string val, string access = "R")
            => new(group, tag, val, LineMailboxProtocol.ClassifyQuality(tag, false), access, time);

        TagMatrixTable.ItemsSource = new[]
        {
            T("GLOBAL", "GVL_MD200_IO.bEStopFeedback", "FALSE (unverified)"),
            T("GLOBAL", "MAIN.bSystemStopRequested", "TRUE (fail-closed)"),
            T("GLOBAL", "MAIN.bSystemEStopOK", "FALSE (unverified)"),
            T("GLOBAL", "MAIN.bPlcQConnected", "FALSE"),
            T("CONVEYOR", "MAIN.rConveyorSpeed", "0.00 mm/s"),
            T("CONVEYOR", "MAIN.rConveyorDist", "0.00 mm"),
            T("CONVEYOR", "MAIN.bMD200_Start", "FALSE"),
            T("CONVEYOR", "MAIN.rMD200_SetFrequency_Hz", "0.00 Hz derived"),
            T("CONVEYOR", "MAIN.rMD200_Ramped_Hz", "0.00 Hz"),
            T("CONVEYOR", "MAIN.rMD200_SetSpeed_mm_s", $"{_presetSpeedMmS:F2} mm/s requested"),
            T("CONVEYOR", "MAIN.rBeltAppliedSpeed_mm_s", "0.00 mm/s accepted"),
            T("CONVEYOR", "MAIN.bBeltSpeedMapReady", "FALSE"),
            T("CONVEYOR", "MAIN.nBeltSpeedMapState", "NOT COMMISSIONED"),
            T("CONVEYOR", "MAIN.nBeltCommandResult", "0"),
            T("CONVEYOR", "MAIN.udiBeltLastAckSeq", "0"),
            T("HMI", "MAIN.bHmiLinkFresh", "FALSE"),
            T("HMI", "MAIN.nLineCommandResult", "0"),
            T("HMI", "MAIN.udiLineLastAckSeq", "0"),
            T("HMI", "MAIN.bLineAutoAccepted", "FALSE"),
            T("HMI", "MAIN.bLineNormalStopLatched", "FALSE"),
            T("HMI", "MAIN.bHmiHeartbeat", "FALSE", "W"),
            T("HMI", "MAIN.bHmiLineStopReq", "FALSE (disarmed)", "W"),
            T("HMI", "MAIN.bHmiLineResetReq", "FALSE (disarmed)", "W"),
            T("HMI", "MAIN.udiHmiLineCmdSeq", "0", "W"),
            T("HMI", "MAIN.udiHmiBeltCmdSeq", "0", "W"),
            T("HMI", "MAIN.bHmiBeltSetReq", "FALSE (disarmed)", "W"),
            T("HMI", "MAIN.bHmiBeltStartReq", "FALSE (disarmed)", "W"),
            T("HMI", "MAIN.bHmiBeltStopReq", "FALSE (disarmed)", "W"),
            T("HMI", "MAIN.rHmiBeltSetSpeed_mm_s", $"{_presetSpeedMmS:F1} mm/s", "W"),
            T("HMI", "MAIN.rBeltMeasuredMmPerHz", "0.0000 mm/s/Hz"),
            T("HMI", "MAIN.rBeltCalculatedSet_Hz", "0.00 Hz derived"),
            T("CONVEYOR", "MAIN.rMD200_AO_VoltsVirtual", "0.00 V calc"),
            T("CONVEYOR", "GVL_MD200_IO.nAO1_Raw", "0"),
            T("CONVEYOR", "GVL_MD200_IO.bRUN_DO", "FALSE"),
            T("GANTRY", "MAIN.bVisionFeedVerified", "FALSE"),
            T("GANTRY", "MAIN.bPlcQConnected", "FALSE"),
            T("GANTRY", "MAIN.bPlcQError", "FALSE"),
            T("GANTRY", "MAIN.nPlcQErrorID", "0"),
            T("GANTRY", "MAIN.nQueueCount", "0"),
            T("GANTRY", "MAIN.nQueueHeadProductID", "0"),
            T("GANTRY", "MAIN.nQueueAccepted", "0"),
            T("GANTRY", "MAIN.nQueueMissed", "0"),
            T("GANTRY", "MAIN.nQueueRealCompleted", "0"),
            T("GANTRY", "MAIN.rQueueHeadX_mm", "0.0 mm"),
            T("GANTRY", "MAIN.rQueueHeadY_mm", "0.0 mm"),
            T("GANTRY", "MAIN.fbPublisher.rTargetX_mm", "0.0 mm"),
            T("GANTRY", "MAIN.fbPublisher.rTargetY_mm", "0.0 mm"),
            T("GANTRY", "MAIN.bLegacyMotionPublisherAllow", "FALSE"),
            T("GANTRY", "MAIN.bGlueCycleBusy", "FALSE"),
            T("GANTRY", "MAIN.bGlueCycleDone", "FALSE"),
            T("GANTRY", "MAIN.bGlueCycleError", "FALSE"),
            T("GANTRY", "MAIN.nGlueCurVertex", "0"),
            T("GANTRY", "MAIN.nGlueErrorCode", "0"),
            T("GANTRY", "MAIN.nGlueCommErrorID", "0"),
            T("GANTRY", "MAIN.bStopCommandConfirmed", "FALSE"),
            T("GANTRY", "MAIN.nAutoDispatchState", "0"),
            new("SCARA", "GVL_SCARA.*", "--", "RESERVED", "--", "--"),
            new("CAMERA 2", "GVL_CAMERA2.*", "--", "RESERVED", "--", "--")
        };
    }

    private void DashCamera_Click(object sender,RoutedEventArgs e) => MainNavigation.SelectedItem=GantryDetailTab;
    private void DashPLC_Click(object sender,RoutedEventArgs e) =>
        MainNavigation.SelectedItem=AlarmsLogsTab;

    // Never permit a second PLC writer in the legacy/manual tag panel.
    // Hardware Emergency Stop is independent of this screen and software STOP.
    private void DashArmChanged(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded)return;
        if (!_conveyorCalibrationMode)
        {
            _dashControlArmed = false;
            DashControlArm.IsChecked = false;
            if (OvCalibrationArm != null) OvCalibrationArm.IsChecked = false;
            DashStartButton.IsEnabled = false;
            DashStopButton.IsEnabled = _dashStatus?.BeltRun == true; // keep STOP enabled if belt is running
            DashControlNote.Text = "CONVEYOR CONTROLS INACTIVE: Select 'CONVEYOR SUBSYSTEM CALIBRATION' on LINE OVERVIEW to enable conveyor running.";
            return;
        }

        _dashControlArmed = DashControlArm.IsChecked == true;
        if (OvCalibrationArm != null && OvCalibrationArm.IsChecked != _dashControlArmed)
            OvCalibrationArm.IsChecked = _dashControlArmed;

        bool isRunning = _dashStatus?.BeltRun == true;
        DashStartButton.IsEnabled = _dashControlArmed && !isRunning;
        DashStopButton.IsEnabled = _dashControlArmed || isRunning; // CRIT-03
        UpdateControlSafetyBanner();

        if (isRunning)
        {
            DashControlNote.Text = $"CONVEYOR CALIBRATION RUNNING at {_dashStatus!.BeltSpeed:F1} mm/s · ISOLATED BELT RUN (NO ROBOT/GLUE) · In danger use PHYSICAL E-STOP.";
        }
        else if (_dashControlArmed)
        {
            DashControlNote.Text = "CONVEYOR CALIBRATION ARMED: Ready to command conveyor (1..150 mm/s) · ISOLATED BELT RUN (NO ROBOT/GLUE).";
        }
        else
        {
            DashControlNote.Text = "CONVEYOR CALIBRATION DISARMED: Operator confirmation required to arm buttons.";
        }
        RefreshInterlockUi();
    }

    private async void DashStart_Click(object sender,RoutedEventArgs e)
    {
        if (!_conveyorCalibrationMode)
        {
            string blocked = "START BLOCKED: Conveyor start is permitted ONLY in 'CONVEYOR SUBSYSTEM CALIBRATION' mode. Select mode on Overview.";
            DashControlNote.Text = blocked;
            StatusText.Text = blocked;
            if (OvStartReason != null) OvStartReason.Text = blocked;
            return;
        }

        bool adsFresh = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        bool mapReady = _dashStatus?.BeltSpeedMapReady == true;
        bool symbolsPresent = _dashStatus?.MailboxSymbolsAvailable == true;
        bool sourceApproved = LineMailboxProtocol.ProductionCommandSourceApproved;
        bool auxEstopOk = _dashStatus?.EstopOk == true;
        bool globalStop = _dashStatus?.GlobalStop == true;
        bool heartbeatFresh = _dashStatus?.HmiLinkFresh == true;
        bool stopLatched = _dashStatus?.LineNormalStopLatched == true;
        bool speedOk = !double.IsNaN(_presetSpeedMmS) && _presetSpeedMmS >= LineSpeedPreset.MinMmS && _presetSpeedMmS <= LineSpeedPreset.MaxMmS;

        string? failReason = null;
        if (!adsFresh) failReason = "CONVEYOR CALIBRATION BLOCKED: ADS 851 offline or stale telemetry.";
        else if (!symbolsPresent) failReason = "CONVEYOR CALIBRATION BLOCKED: HMI PLC mailbox symbols missing on runtime.";
        else if (!sourceApproved) failReason = "CONVEYOR CALIBRATION BLOCKED: Production command source is not approved.";
        else if (!auxEstopOk) failReason = "CONVEYOR CALIBRATION BLOCKED: Emergency stop auxiliary contact (ST-1228 DI0) not healthy. In danger use PHYSICAL E-STOP.";
        else if (globalStop && !stopLatched) failReason = "CONVEYOR CALIBRATION BLOCKED: Global stop active in PLC. In danger use PHYSICAL E-STOP.";
        else if (!heartbeatFresh) failReason = "CONVEYOR CALIBRATION BLOCKED: HMI watchdog heartbeat stale in PLC.";
        else if (stopLatched) failReason = "CONVEYOR CALIBRATION BLOCKED: Normal stop latched in PLC; reset required.";
        else if (globalStop) failReason = "CONVEYOR CALIBRATION BLOCKED: Global stop active in PLC. In danger use PHYSICAL E-STOP.";
        else if (!mapReady) failReason = "CONVEYOR CALIBRATION BLOCKED: Belt speed map not ready in PLC.";
        else if (!speedOk) failReason = $"CONVEYOR CALIBRATION BLOCKED: Speed {_presetSpeedMmS:F1} mm/s outside [1.0..150.0].";
        else if (!_dashControlArmed) failReason = "CONVEYOR CALIBRATION BLOCKED: Arm checkbox required to enable start.";

        if (failReason != null)
        {
            DashControlNote.Text = failReason;
            StatusText.Text = failReason;
            if (OvStartReason != null)
            {
                OvStartReason.Text = failReason;
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            RefreshInterlockUi();
            return;
        }

        // DEF-03: prevent Overview calibration START/STOP async re-entrancy; disable both buttons during dispatch
        DashStartButton.IsEnabled = false;
        if (SysStartButton != null) SysStartButton.IsEnabled = false;
        if (OvStartReason != null) OvStartReason.Text = "CONVEYOR CALIBRATION START DISPATCHING: Asserting start request to PLC...";

        try {
            if (!LineMailboxGateway.Instance.PriorBeltSetAccepted || Math.Abs(LineMailboxGateway.Instance.LastAcceptedBeltSetMmS - _presetSpeedMmS) > 0.05)
            {
                var setResult = await LineMailboxGateway.Instance.RequestManualBeltSetAsync(false, adsFresh, _presetSpeedMmS);
                if (!setResult.Success)
                {
                    string setMsg = $"START BLOCKED: Speed SET failed: {setResult.Message}";
                    DashControlNote.Text = setMsg;
                    StatusText.Text = setMsg;
                    if (OvStartReason != null)
                    {
                        OvStartReason.Text = setMsg;
                        OvStartReason.Foreground = DashBrush("#FF6E70");
                    }
                    if (CvPresetStatus != null) CvPresetStatus.Text = $"SET FAILED: {setResult.Message}";
                    if (OvPresetError != null) OvPresetError.Text = setResult.Message;
                    return;
                }
            }
            var result = await LineMailboxGateway.Instance.RequestManualBeltStartAsync(false, adsFresh, mapReady, _presetSpeedMmS);
            DashControlNote.Text = result.Message;
            StatusText.Text = result.Message;
            if (OvStartReason != null)
            {
                OvStartReason.Text = result.Message;
                OvStartReason.Foreground = DashBrush(result.Success ? "#39C7A2" : "#FF6E70");
            }
            SensorHistory("CONVEYOR SUBSYSTEM CALIBRATION START: " + result.Message);
            await RefreshDashboardAsync();
        } finally {
            RefreshInterlockUi();
        }
    }

    private async void DashStop_Click(object sender,RoutedEventArgs e)
    {
        bool adsFresh = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));

        // DEF-03: prevent Overview calibration START/STOP async re-entrancy; disable both buttons during dispatch
        DashStopButton.IsEnabled = false;
        if (SysStopButton != null) SysStopButton.IsEnabled = false;
        if (OvStartReason != null) OvStartReason.Text = "CONVEYOR CALIBRATION STOP DISPATCHING: Asserting stop request to PLC...";

        try {
            var result = await LineMailboxGateway.Instance.RequestManualBeltStopAsync(false, adsFresh);
            DashControlNote.Text = result.Message;
            StatusText.Text = result.Message;
            if (OvStartReason != null)
            {
                OvStartReason.Text = result.Message;
                OvStartReason.Foreground = DashBrush(result.Success ? "#39C7A2" : "#FF6E70");
            }
            SensorHistory("CONVEYOR SUBSYSTEM CALIBRATION STOP: " + result.Message);
            await RefreshDashboardAsync();
        } finally {
            RefreshInterlockUi();
        }
    }
    private InterlockInputs BuildInterlockInputs()
    {
        var st = _dashStatus;
        bool cameraConnected = _video != null || _cam1.IsOpen;
        DateTime cameraFrameUtc = _video != null ? _stationLastFrameUtc : _cam1.Model.LastFrameUtc;
        bool cameraFresh = cameraConnected && cameraFrameUtc != default
            && DateTime.UtcNow - cameraFrameUtc <= TimeSpan.FromSeconds(3);
        return new InterlockInputs(
            AdsOnline: st is not null && LineMailboxProtocol.IsTelemetryFresh(st.ReceivedUtc,DateTime.UtcNow,TimeSpan.FromSeconds(3)),
            EstopOk: st?.EstopOk == true,
            GlobalStop: st?.GlobalStop == true,
            CameraConnected: cameraConnected,
            CameraFrameFresh: cameraFresh,
            PlcQConnected: st?.PlcQConnected == true,
            GantryFault: st?.GantryError == true,
            GantryBusy: st?.GantryBusy == true,
            PresetSpeedMmS: _presetSpeedMmS,
            AutoContractCommissioned: LineAutoContractCommissioned && LineMailboxProtocol.ProductionCommandSourceApproved && st?.MailboxSymbolsAvailable==true,
            ScaraExcluded: true);
    }

    private void RefreshInterlockUi()
    {
        if (_closing) return;
        var inputs = BuildInterlockInputs();
        var evaluation = _lineInterlock.Evaluate(inputs);

        if (_conveyorCalibrationMode)
        {
            var st = _dashStatus;
            bool adsFresh = st is not null && LineMailboxProtocol.IsTelemetryFresh(st.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
            bool symbolsPresent = st?.MailboxSymbolsAvailable == true;
            bool sourceApproved = LineMailboxProtocol.ProductionCommandSourceApproved;
            bool auxEstopOk = st?.EstopOk == true;
            bool globalStop = st?.GlobalStop == true;
            bool heartbeatFresh = st?.HmiLinkFresh == true;
            bool stopLatched = st?.LineNormalStopLatched == true;
            bool mapReady = st?.BeltSpeedMapReady == true;
            bool speedOk = !double.IsNaN(inputs.PresetSpeedMmS) && inputs.PresetSpeedMmS >= LineSpeedPreset.MinMmS && inputs.PresetSpeedMmS <= LineSpeedPreset.MaxMmS;
            bool isRunning = st?.BeltRun == true;

            OvIlAds.Text = (adsFresh ? "● " : "○ ") + "ADS 851";
            OvIlAds.Foreground = DashBrush(adsFresh ? "#39C7A2" : "#FF6E70");
            OvIlAds.ToolTip = adsFresh ? "TwinCAT ADS router connected (port 851)" : "ADS 851 OFFLINE / STALE";

            OvIlEstop.Text = (auxEstopOk ? "● " : "○ ") + "E-STOP AUX";
            OvIlEstop.Foreground = DashBrush(auxEstopOk ? "#39C7A2" : "#FF6E70");
            OvIlEstop.ToolTip = auxEstopOk ? "E-stop auxiliary contact healthy (ST-1228 DI0)" : "Emergency stop active or auxiliary feedback not healthy";

            OvIlSpeed.Text = (speedOk ? "● " : "○ ") + "SPEED PRESET";
            OvIlSpeed.Foreground = DashBrush(speedOk ? "#39C7A2" : "#FF6E70");
            OvIlSpeed.ToolTip = $"Calibration speed {inputs.PresetSpeedMmS:F1} mm/s";

            // Camera is not required for independent conveyor subsystem calibration
            OvIlCamera.Text = "● CAM (NOT REQUIRED)";
            OvIlCamera.Foreground = DashBrush("#27C9EC");
            OvIlCamera.ToolTip = "CONVEYOR SUBSYSTEM CALIBRATION: Independent belt drive tuning does not require camera acquisition.";

            // Robot / PLC-Q / Gantry / SCARA / AUTO are inhibited
            OvIlPlcQ.Text = "● PLC-Q (INHIBITED)";
            OvIlPlcQ.Foreground = DashBrush("#27C9EC");
            OvIlPlcQ.ToolTip = "CONVEYOR SUBSYSTEM CALIBRATION: Robot MC bridge and tracking queue are disarmed.";

            OvIlGantry.Text = "● GANTRY (INHIBITED)";
            OvIlGantry.Foreground = DashBrush("#27C9EC");
            OvIlGantry.ToolTip = "CONVEYOR SUBSYSTEM CALIBRATION: Gantry motion and glue dispense are inhibited.";

            OvIlMaster.Text = "● AUTO INHIBITED";
            OvIlMaster.Foreground = DashBrush("#27C9EC");
            OvIlMaster.ToolTip = "CONVEYOR SUBSYSTEM CALIBRATION: Production AUTO coordinator is disarmed. Production gates are NOT bypassed; line auto contract remains separate and uncommissioned.";

            OvIlScara.Text = "● SCARA (INHIBITED)";
            OvIlScara.Foreground = DashBrush("#27C9EC");
            OvIlScara.ToolTip = "CONVEYOR SUBSYSTEM CALIBRATION: SCARA Station 02 is disarmed.";

            bool canStartCalibration = _dashControlArmed && adsFresh && symbolsPresent && sourceApproved && auxEstopOk && !globalStop && heartbeatFresh && !stopLatched && mapReady && speedOk && !isRunning;

            SysStartButton.IsEnabled = canStartCalibration;
            SysStopButton.IsEnabled = _dashControlArmed || isRunning;

            var calibResetInputs = new ConveyorCalibrationResetInputs(
                AdsFresh: adsFresh,
                AuxEstopOk: auxEstopOk,
                GlobalStop: globalStop,
                NormalStopLatched: stopLatched,
                BeltRunning: isRunning || st?.BeltRequest == true || LineMailboxGateway.Instance.ManualBeltActive,
                GlueActive: st?.GantryBusy == true || st?.GantryError == true,
                AutoActive: st?.LineAutoAccepted == true || LineMailboxGateway.Instance.AutoLineActive);
            var calibResetEval = ConveyorCalibrationInterlock.EvaluateReset(calibResetInputs);
            bool canResetCalibration = calibResetEval.CanReset;
            SysResetButton.IsEnabled = canResetCalibration;

            if (OvCommandCommissionStatus != null)
            {
                if (!sourceApproved || !symbolsPresent)
                {
                    OvCommandCommissionStatus.Text = "CALIBRATION MAILBOX · NOT COMMISSIONED";
                    OvCommandCommissionStatus.Foreground = DashBrush("#FF6E70");
                }
                else
                {
                    OvCommandCommissionStatus.Text = "CALIBRATION MAILBOX LIVE · ISOLATED (NO ROBOT/GLUE)";
                    OvCommandCommissionStatus.Foreground = DashBrush("#27C9EC");
                }
            }

            // Prioritize actual running state (MIN-02)
            OvCvCommand.Text = isRunning ? "CALIB / RUNNING" : (_dashControlArmed ? "CALIB / ARMED" : "CALIB / DISARMED");

            // Complete fail-closed reason hierarchy (IMP-01/02)
            if (!adsFresh)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: ADS 851 offline or stale telemetry.";
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            else if (!symbolsPresent)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: HMI PLC mailbox symbols missing on runtime.";
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            else if (!sourceApproved)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: Production command source is not approved.";
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            else if (!auxEstopOk)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: Emergency stop auxiliary contact (ST-1228 DI0) not healthy. In danger use PHYSICAL E-STOP.";
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            else if (globalStop && !stopLatched)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: Global stop active in PLC. In danger use PHYSICAL E-STOP.";
                OvStartReason.Foreground = DashBrush("#FF6E70");
            }
            else if (!heartbeatFresh)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: HMI watchdog heartbeat stale in PLC.";
                OvStartReason.Foreground = DashBrush("#F4BB45");
            }
            else if (stopLatched)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: Normal stop latched in PLC; reset required.";
                OvStartReason.Foreground = DashBrush("#F4BB45");
            }
            else if (!mapReady)
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION BLOCKED: Belt speed map not ready in PLC.";
                OvStartReason.Foreground = DashBrush("#F4BB45");
            }
            else if (!speedOk)
            {
                OvStartReason.Text = $"CONVEYOR CALIBRATION BLOCKED: Speed {inputs.PresetSpeedMmS:F1} mm/s outside [1.0..150.0].";
                OvStartReason.Foreground = DashBrush("#F4BB45");
            }
            else if (isRunning)
            {
                OvStartReason.Text = $"CONVEYOR CALIBRATION RUNNING at {st!.BeltSpeed:F1} mm/s · ISOLATED BELT RUN (NO ROBOT/GLUE) · In danger use PHYSICAL E-STOP.";
                OvStartReason.Foreground = DashBrush("#39C7A2");
            }
            else if (_dashControlArmed)
            {
                OvStartReason.Text = $"CONVEYOR CALIBRATION ARMED at {_presetSpeedMmS:F1} mm/s · Ready to start · ISOLATED BELT RUN (NO ROBOT/GLUE)";
                OvStartReason.Foreground = DashBrush("#39C7A2");
            }
            else
            {
                OvStartReason.Text = "CONVEYOR CALIBRATION READY (DISARMED): Check 'Arm Subsystem Calibration' to enable start · ISOLATED BELT RUN (NO ROBOT/GLUE)";
                OvStartReason.Foreground = DashBrush("#F4BB45");
            }
            return;
        }

        void Mark(System.Windows.Controls.TextBlock block, InterlockCheckId id)
        {
            var check = evaluation.Checks.FirstOrDefault(c => c.Id == id);
            if (check is null) return;
            block.Text = (check.Ok ? "● " : "○ ") + check.Label.ToUpperInvariant();
            block.Foreground = DashBrush(check.Ok ? "#39C7A2"
                : evaluation.State == InterlockState.Offline ? "#FF6E70" : "#F4BB45");
            block.ToolTip = check.Detail;
        }
        Mark(OvIlAds, InterlockCheckId.AdsLink);
        Mark(OvIlEstop, InterlockCheckId.Estop);
        Mark(OvIlPlcQ, InterlockCheckId.PlcQ);
        Mark(OvIlGantry, InterlockCheckId.GantryFault);
        Mark(OvIlCamera, InterlockCheckId.CameraFrame);
        Mark(OvIlSpeed, InterlockCheckId.SpeedSetpoint);
        Mark(OvIlMaster, InterlockCheckId.AutoContract);
        Mark(OvIlScara, InterlockCheckId.ScaraExcluded);

        SysStartButton.IsEnabled = evaluation.CanStart;
        SysStopButton.IsEnabled = evaluation.CanStop;
        SysResetButton.IsEnabled = evaluation.CanReset;
        OvCvCommand.Text = LineStateText(evaluation.State);
        OvStartReason.Text = evaluation.PrimaryReason;
        if (_dashStatus is { } blockedStatus)
        {
            var notes = new List<string>();
            if(!blockedStatus.MailboxSymbolsAvailable)
                notes.Add("PLC HMI TAGS MISSING");
            if(!blockedStatus.BeltSpeedMapReady)
                notes.Add("BELT "+LineMailboxProtocol.DescribeSpeedMap(blockedStatus.BeltSpeedMapState));
            if(!blockedStatus.HmiLinkFresh)
                notes.Add("HMI HEARTBEAT STALE");
            if(blockedStatus.LineNormalStopLatched)
                notes.Add("NORMAL STOP LATCHED");
            if(!LineMailboxProtocol.ProductionCommandSourceApproved)
            {
                bool writeConnected = LineMailboxGateway.Instance.Transport.IsConnected;
                notes.Add(writeConnected ? "WRITE TRANSPORT: CONNECTED (PORT 851) · DISARMED" : "WRITE TRANSPORT: DISCONNECTED · DISARMED");
            }
            else
            {
                bool writeConnected = LineMailboxGateway.Instance.Transport.IsConnected;
                notes.Add(writeConnected ? "WRITE TRANSPORT: LIVE (PORT 851) · COMMISSIONED" : "WRITE TRANSPORT: RECONNECTING");
            }
            if(notes.Count > 0)
                OvStartReason.Text += " | " + string.Join(" | ", notes);
        }
        OvStartReason.Foreground = DashBrush(evaluation.CanStart ? "#39C7A2"
            : evaluation.State == InterlockState.Offline ? "#FF6E70" : "#F4BB45");

        if (OvCommandCommissionStatus != null)
        {
            OvCommandCommissionStatus.Text = "PRODUCTION AUTO · COMMISSIONING PENDING";
            OvCommandCommissionStatus.Foreground = DashBrush("#F4BB45");
        }
    }

    private static string LineStateText(InterlockState state) => state switch
    {
        InterlockState.Offline => "OFFLINE",
        InterlockState.Ready => "READY TO REQUEST",
        InterlockState.StartRequested => "START REQUESTED / ACK PENDING",
        InterlockState.RunningAcknowledged => "RUNNING ACKNOWLEDGED",
        InterlockState.StopRequested => "STOP REQUESTED",
        InterlockState.Fault => "FAULT",
        _ => "NOT READY"
    };

    private async Task ApplyPresetSpeed(System.Windows.Controls.TextBox source, System.Windows.Controls.TextBox mirror)
    {
        if (!LineSpeedPreset.TryParse(source.Text, out var mmS, out var error))
        {
            OvPresetError.Text = error;
            OvPresetHz.Text = LineSpeedPreset.NominalPreviewHz(_presetSpeedMmS);
            return;
        }
        _presetSpeedMmS = mmS;
        OvPresetError.Text = "";
        string normalized = mmS.ToString("0.#", CultureInfo.InvariantCulture);
        source.Text = normalized;
        if (!ReferenceEquals(source, mirror)) mirror.Text = normalized;
        string hz = LineSpeedPreset.NominalPreviewHz(mmS);
        if (CvManualSpeedDisplay is not null) CvManualSpeedDisplay.Text = $"{normalized} mm/s";
        OvPresetHz.Text = hz; // Estimated only, never sent to the MD200.
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        var setResult = await LineMailboxGateway.Instance.RequestManualBeltSetAsync(false, adsOnline, mmS);
        if (setResult.Success)
        {
            CvPresetStatus.Text = $"SET ACCEPTED: {normalized} mm/s · {hz}";
            OvPresetError.Text = "";
        }
        else
        {
            CvPresetStatus.Text = $"SET FAILED: {setResult.Message}";
            OvPresetError.Text = setResult.Message;
        }
        RefreshInterlockUi();
        await RefreshDashboardAsync();
    }

    private async void PresetSpeedApply_Click(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(OvPresetSpeedBox, CvPresetSpeedBox);
    private async void PresetSpeed_LostFocus(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(OvPresetSpeedBox, CvPresetSpeedBox);
    private async void CvPresetSpeedApply_Click(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(CvPresetSpeedBox, OvPresetSpeedBox);
    private async void CvPresetSpeed_LostFocus(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(CvPresetSpeedBox, OvPresetSpeedBox);


    private async void SysLineStart_Click(object sender, RoutedEventArgs e)
    {
        if (_conveyorCalibrationMode)
        {
            DashStart_Click(sender, e);
            return;
        }
        var (interlockOk, interlockMsg) = _lineInterlock.RequestStart(BuildInterlockInputs());
        if (!interlockOk)
        {
            OvStartReason.Text = "START BLOCKED: " + interlockMsg;
            StatusText.Text = "LINE START BLOCKED: " + interlockMsg;
            RefreshInterlockUi();
            return;
        }

        bool camera1Ready = _cam1.IsOpen || _video != null;
        bool trackingReady = _trackingEnabled;
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        bool symbolsAvailable = _dashStatus?.MailboxSymbolsAvailable == true;

        SysStartButton.IsEnabled = false;
        try
        {
            var result = await LineMailboxGateway.Instance.RequestLineStartAsync(false, adsOnline, symbolsAvailable, camera1Ready, trackingReady);
            if (!result.Success)
            {
                OvStartReason.Text = "START BLOCKED: " + result.Message;
                StatusText.Text = result.Message;
                RefreshInterlockUi();
                return;
            }

            if (!_trackingEnabled) StartTracking_Click(sender, e);


            StatusText.Text = result.Message;
            SensorHistory("LINE START REQUEST: " + result.Message);
            RefreshInterlockUi();
            await RefreshDashboardAsync();
        }
        finally
        {
            RefreshInterlockUi();
        }
    }

    private async void SysLineStop_Click(object sender, RoutedEventArgs e)
    {
        if (_conveyorCalibrationMode)
        {
            DashStop_Click(sender, e);
            return;
        }
        var (interlockOk, interlockMsg) = _lineInterlock.RequestStop(BuildInterlockInputs());
        if (!interlockOk)
        {
            OvStartReason.Text = "STOP BLOCKED: " + interlockMsg;
            StatusText.Text = "LINE STOP BLOCKED: " + interlockMsg;
            RefreshInterlockUi();
            return;
        }

        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        SysStopButton.IsEnabled = false;
        try
        {
            var result = await LineMailboxGateway.Instance.RequestLineStopAsync(false, adsOnline);
            if (_trackingEnabled) StopTracking_Click(sender, e);
            StatusText.Text = result.Message;
            SensorHistory("LINE STOP REQUEST: normal software stop, NOT a certified emergency stop");
            RefreshInterlockUi();
            await RefreshDashboardAsync();
        }
        finally
        {
            RefreshInterlockUi();
        }
    }

    private async void SysLineReset_Click(object sender, RoutedEventArgs e)
    {
        if (_conveyorCalibrationMode)
        {
            var st = _dashStatus;
            bool calibAdsOnline = st is not null && LineMailboxProtocol.IsTelemetryFresh(st.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
            bool isRunning = st?.BeltRun == true || LineMailboxGateway.Instance.ManualBeltActive;
            bool stopLatched = st?.LineNormalStopLatched == true;

            var resetInputs = new ConveyorCalibrationResetInputs(
                AdsFresh: calibAdsOnline,
                AuxEstopOk: st?.EstopOk == true,
                GlobalStop: st?.GlobalStop == true,
                NormalStopLatched: stopLatched,
                BeltRunning: isRunning || st?.BeltRequest == true,
                GlueActive: st?.GantryBusy == true || st?.GantryError == true,
                AutoActive: st?.LineAutoAccepted == true || LineMailboxGateway.Instance.AutoLineActive);

            var resetEval = ConveyorCalibrationInterlock.EvaluateReset(resetInputs);
            if (!resetEval.CanReset)
            {
                string msg = resetEval.Reason;
                if (OvStartReason != null) { OvStartReason.Text = msg; OvStartReason.Foreground = DashBrush(stopLatched ? "#F4BB45" : "#FF6E70"); }
                StatusText.Text = msg;
                RefreshInterlockUi();
                return;
            }

            SysResetButton.IsEnabled = false;
            try
            {
                var result = await LineMailboxGateway.Instance.RequestLineResetAsync(false, calibAdsOnline);
                string resultMsg = (result.Success ? "CALIBRATION RESET: " : "CALIBRATION RESET BLOCKED: ") + result.Message;
                if (OvStartReason != null)
                {
                    OvStartReason.Text = resultMsg;
                    OvStartReason.Foreground = DashBrush(result.Success ? "#39C7A2" : "#FF6E70");
                }
                StatusText.Text = resultMsg;
                SensorHistory("CONVEYOR CALIBRATION RESET: " + result.Message);
                await RefreshDashboardAsync();
            }
            finally
            {
                RefreshInterlockUi();
            }
            return;
        }

        var (interlockOk, interlockMsg) = _lineInterlock.RequestReset(BuildInterlockInputs());
        if (!interlockOk)
        {
            OvStartReason.Text = "RESET BLOCKED: " + interlockMsg;
            StatusText.Text = "LINE RESET BLOCKED: " + interlockMsg;
            RefreshInterlockUi();
            return;
        }
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        SysResetButton.IsEnabled = false;
        try
        {
            var result = await LineMailboxGateway.Instance.RequestLineResetAsync(false, adsOnline);
            OvStartReason.Text = (result.Success ? "RESET: " : "RESET BLOCKED: ") + result.Message;
            StatusText.Text = OvStartReason.Text;
            SensorHistory("LINE RESET: " + result.Message);
            RefreshInterlockUi();
            await RefreshDashboardAsync();
        }
        finally
        {
            RefreshInterlockUi();
        }
    }

}
