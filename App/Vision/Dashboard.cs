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
        _dashTimer.Tick+=DashTimer_Tick;
        _dashTimer.Start();
        _heartbeatTimer.Tick+=HeartbeatTimer_Tick;
        _heartbeatTimer.Start();
        Closed+=(_,_)=>{
            _dashTimer.Stop();
            _heartbeatTimer.Stop();
            LineMailboxGateway.Instance.DisconnectTransport();
        };
    }
    private void HeartbeatTimer_Tick(object? sender,EventArgs e)
    {
        if(_closing)return;
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        LineMailboxGateway.Instance.TickHeartbeat(false, adsOnline);
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
            DashGantryPlc.Text="Mitsubishi Q: "+(s.PlcQConnected?"TCP ONLINE":"DISCONNECTED");
            DashGantryState.Text=s.GantryError?"GANTRY: FAULT / NOT READY"
                :s.GantryBusy?"GANTRY: CYCLE IN PROGRESS"
                :s.GantryPublisherEnabled?"PUBLISHER ENABLED / NOT COMMISSIONED":"GANTRY: AUTO NOT COMMISSIONED";
            DashGantryVertex.Text=$"Current vertex: {s.GantryVertex} / 4 | Error: {s.GantryErrorCode}";
            DashGantryStats.Text=$"FIFO: {s.QueueCount}  |  Accepted: {s.Accepted}  |  Missed: {s.Missed}";
            DashCamera1.Text=(_cam1.IsOpen?"Camera 1: LIVE":"Camera 1: "+(_video!=null?"VIDEO TEST":"DISCONNECTED"))
                 +(_trackingEnabled?" | DETECT ON":" | DETECT OFF");
            // Camera connection ≠ validated camera-to-PLC calibration.
            if(s.GlobalStop || !s.EstopOk)
                DashMachineMessage.Text="GLOBAL STOP REQUESTED · DEVICE START BLOCKED";
            else if(!s.PlcQConnected)
                DashMachineMessage.Text="AUX E-STOP DI OK · MITSUBISHI PLC-Q DISCONNECTED · GANTRY BLOCKED";
            else
                DashMachineMessage.Text="AUX E-STOP DI OK · TWINCAT RUN · GANTRY AUTO NOT COMMISSIONED";
            // Manual conveyor panel: arming toggle is operator-controlled; buttons follow arm state
            DashControlArm.IsEnabled=true;
            DashStartButton.IsEnabled=_dashControlArmed;
            DashStopButton.IsEnabled=_dashControlArmed;
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
            DashControlNote.Text = reason + extra;
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
            T("GLOBAL","MAIN.bServo_Ready",V(s.ServoReady)),
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
            T("GLOBAL", "MAIN.bServo_Ready", "FALSE"),
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
            T("GANTRY", "MAIN.nQueueCount", "0"),
            T("GANTRY", "MAIN.nQueueHeadProductID", "0"),
            T("GANTRY", "MAIN.nQueueAccepted", "0"),
            T("GANTRY", "MAIN.nQueueMissed", "0"),
            T("GANTRY", "MAIN.bLegacyMotionPublisherAllow", "FALSE"),
            T("GANTRY", "MAIN.bGlueCycleBusy", "FALSE"),
            T("GANTRY", "MAIN.bGlueCycleDone", "FALSE"),
            T("GANTRY", "MAIN.bGlueCycleError", "FALSE"),
            T("GANTRY", "MAIN.nGlueCurVertex", "0"),
            T("GANTRY", "MAIN.nGlueErrorCode", "0"),
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
        _dashControlArmed=DashControlArm.IsChecked==true;
        DashStartButton.IsEnabled=_dashControlArmed;
        DashStopButton.IsEnabled=_dashControlArmed;
        DashControlNote.Text=_dashControlArmed
            ? (LineMailboxProtocol.ProductionCommandSourceApproved ? "MANUAL ARMED: Ready to command conveyor (1..150 mm/s)." : "MANUAL ARMED: Physical writes disarmed pending commissioning.")
            : "MANUAL DISARMED: Operator confirmation required to arm buttons.";
    }

    private async void DashStart_Click(object sender,RoutedEventArgs e)
    {
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        bool mapReady = _dashStatus?.BeltSpeedMapReady == true;
        DashStartButton.IsEnabled = false;
        try {
            if (!LineMailboxGateway.Instance.PriorBeltSetAccepted || Math.Abs(LineMailboxGateway.Instance.LastAcceptedBeltSetMmS - _presetSpeedMmS) > 0.05)
            {
                var setResult = await LineMailboxGateway.Instance.RequestManualBeltSetAsync(false, adsOnline, _presetSpeedMmS);
                if (!setResult.Success)
                {
                    DashControlNote.Text = setResult.Message;
                    StatusText.Text = setResult.Message;
                    return;
                }
            }
            var result = await LineMailboxGateway.Instance.RequestManualBeltStartAsync(false, adsOnline, mapReady, _presetSpeedMmS);
            DashControlNote.Text = result.Message;
            StatusText.Text = result.Message;
            SensorHistory("MANUAL BELT START: " + result.Message);
            await RefreshDashboardAsync();
        } finally {
            DashStartButton.IsEnabled = _dashControlArmed;
        }
    }

    private async void DashStop_Click(object sender,RoutedEventArgs e)
    {
        bool adsOnline = _dashStatus is not null && LineMailboxProtocol.IsTelemetryFresh(_dashStatus.ReceivedUtc, DateTime.UtcNow, TimeSpan.FromSeconds(3));
        DashStopButton.IsEnabled = false;
        try {
            var result = await LineMailboxGateway.Instance.RequestManualBeltStopAsync(false, adsOnline);
            DashControlNote.Text = result.Message;
            StatusText.Text = result.Message;
            SensorHistory("MANUAL BELT STOP: " + result.Message);
            await RefreshDashboardAsync();
        } finally {
            DashStopButton.IsEnabled = _dashControlArmed;
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
        var evaluation = _lineInterlock.Evaluate(BuildInterlockInputs());

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
        if(_dashStatus is { } blockedStatus)
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
            if(!blockedStatus.ServoReady)
                notes.Add("AUTO/GANTRY: SERVO NOT READY (Manual belt independent)");
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
        CvPresetStatus.Text = $"PRESET APPLIED: {normalized} mm/s · {hz} ({setResult.Message})";
        RefreshInterlockUi();
        await RefreshDashboardAsync();
    }

    private async void PresetSpeedApply_Click(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(OvPresetSpeedBox, CvPresetSpeedBox);
    private async void PresetSpeed_LostFocus(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(OvPresetSpeedBox, CvPresetSpeedBox);
    private async void CvPresetSpeedApply_Click(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(CvPresetSpeedBox, OvPresetSpeedBox);
    private async void CvPresetSpeed_LostFocus(object sender, RoutedEventArgs e) => await ApplyPresetSpeed(CvPresetSpeedBox, OvPresetSpeedBox);


    private async void SysLineStart_Click(object sender, RoutedEventArgs e)
    {
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
