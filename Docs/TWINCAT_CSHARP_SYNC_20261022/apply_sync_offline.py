from pathlib import Path
import codecs
root=Path(r"E:\Orca\DATN")
def rewrite(relative, transform):
    p=root/relative
    raw=p.read_bytes()
    bom=raw.startswith(codecs.BOM_UTF8)
    t=raw.decode("utf-8-sig")
    ending="\r\n" if "\r\n" in t else "\n"
    t=t.replace("\r\n","\n")
    orig=t
    t=transform(t)
    if t==orig: raise RuntimeError(f"NO CHANGE: {relative}")
    out=(codecs.BOM_UTF8 if bom else b"") + t.replace("\n",ending).encode("utf-8")
    p.write_bytes(out)
    print("UPDATED",relative,"bytes",len(out))
def single(t,a,b,what):
    n=t.count(a)
    if n!=1: raise RuntimeError(f"{what}: wanted 1 match, found {n}")
    return t.replace(a,b,1)
def plc(t):
    old="TIID^Device 2 (EtherCAT)^Box 1 (NA-9286,Crevis)^Module "
    new="TIID^Device 3 (EtherCAT)^Box 1 (NA-9286,Crevis)^Module "
    if t.count(old)!=3: raise RuntimeError("Need exactly three obsolete live attributes")
    return t.replace(old,new)
rewrite(r"PLC\TwinCAT\DATN\PLC1\GVLs\GVL_MD200_IO.TcGVL",plc)
def dashboard(t):
    t=single(t,"    public ushort AoRaw { get; init; }\n","""    public ushort AoRaw { get; init; }
    public bool HmiLinkFresh { get; init; }
    public bool BeltSpeedMapReady { get; init; }
    public ushort BeltSpeedMapState { get; init; }
    public ushort BeltCommandResult { get; init; }
    public ushort LineCommandResult { get; init; }
    public uint LineAckSequence { get; init; }
    public bool LineAutoAccepted { get; init; }
    public double RequestedSpeedMmS { get; init; }
    public double AppliedSpeedMmS { get; init; }
    public double MeasuredMmPerHz { get; init; }
    public bool MailboxSymbolsAvailable =>
        LineMailboxProtocol.AllReadbacksPresent(SymbolPresence);
""","properties")
    a=t.index("    private static void Write<T>(")
    b=t.index("    private static T Optional<T>(",a)
    t=t[:a]+t[b:]
    t=single(t,'            AoRaw=Read<ushort>(ads,"GVL_MD200_IO.nAO1_Raw")',
    """            AoRaw=Read<ushort>(ads,"GVL_MD200_IO.nAO1_Raw"),
            HmiLinkFresh=Optional<bool>(ads,"MAIN.bHmiLinkFresh",presence),
            BeltSpeedMapReady=Optional<bool>(ads,"MAIN.bBeltSpeedMapReady",presence),
            BeltSpeedMapState=Optional<ushort>(ads,"MAIN.nBeltSpeedMapState",presence),
            BeltCommandResult=Optional<ushort>(ads,"MAIN.nBeltCommandResult",presence),
            LineCommandResult=Optional<ushort>(ads,"MAIN.nLineCommandResult",presence),
            LineAckSequence=Optional<uint>(ads,"MAIN.udiLineLastAckSeq",presence),
            LineAutoAccepted=Optional<bool>(ads,"MAIN.bLineAutoAccepted",presence),
            RequestedSpeedMmS=Optional<double>(ads,"MAIN.rMD200_SetSpeed_mm_s",presence),
            AppliedSpeedMmS=Optional<double>(ads,"MAIN.rBeltAppliedSpeed_mm_s",presence),
            MeasuredMmPerHz=Optional<double>(ads,"MAIN.rBeltMeasuredMmPerHz",presence)""","poll")
    a=t.index("    public static void StartBelt(double hz)")
    b=t.index("\n}\n\npublic partial class MainWindow",a)
    t=t[:a]+"""    // Legacy direct-Hz writes intentionally retired. New command support must use
    // sequenced TwinCAT mailbox after independent commissioning, never this class.
"""+t[b:]
    t=single(t,"        DashControlArm.IsChecked=false;",
    """        DashControlArm.IsChecked=false;
        DashControlArm.IsEnabled=false; // Legacy direct-Hz control retired.
        DashStartButton.IsEnabled=false;
        DashStopButton.IsEnabled=false;
        DashHzInput.IsEnabled=false;
        DashControlNote.Text="LEGACY DIRECT-Hz COMMANDS DISABLED. Use the central mm/s preset; actual machine commands await PLC/HMI commissioning.";""","init")
    a=t.index("            // Normal STOP is available even if command-arm switch")
    b=t.index("        } catch(Exception ex)",a)
    t=t[:a]+"""            // Obsolete direct-Hz controls are always disarmed (even with ADS RUN).
            DashControlArm.IsEnabled=false;
            DashStartButton.IsEnabled=false;
            DashStopButton.IsEnabled=false;
            DashControlNote.Text=LineMailboxProtocol.BlockReason(
                LineMailboxProtocol.IsTelemetryFresh(s.ReceivedUtc,DateTime.UtcNow,TimeSpan.FromSeconds(3)),
                s.MailboxSymbolsAvailable,s.EstopOk,!s.GlobalStop,
                s.BeltSpeedMapReady,s.HmiLinkFresh)
                +" | Legacy direct-Hz commands disabled.";
"""+t[b:]
    t=single(t,'            DashMachineMessage.Text="AUX E-STOP DI OK', '            DashMachineMessage.Text="AUX E-STOP DI OK', 'noop') if False else t
    a=t.index("    private void DashArmChanged(")
    b=t.index("    private InterlockInputs BuildInterlockInputs()",a)
    t=t[:a]+"""    // Never permit a second PLC writer in the legacy/manual tag panel.
    // Hardware Emergency Stop is independent of this screen and software STOP.
    private void DashArmChanged(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded)return;
        _dashControlArmed=false;
        DashControlArm.IsChecked=false;
        DashStartButton.IsEnabled=false;
        DashStopButton.IsEnabled=false;
        DashControlNote.Text="Direct Hz/START control retired. Use central mm/s interface after commissioning.";
    }

    private void DashStart_Click(object sender,RoutedEventArgs e)
    {
        DashControlNote.Text="START BLOCKED: legacy direct-Hz path disabled; speed map and PLC/HMI mailbox require commissioning.";
    }

    private void DashStop_Click(object sender,RoutedEventArgs e)
    {
        DashControlNote.Text="LEGACY STOP path disabled. Normal STOP via PLC mailbox is not yet commissioned. In danger use PHYSICAL E-STOP.";
    }
"""+t[b:]
    t=single(t,"            AdsOnline: st is not null,",
    "            AdsOnline: st is not null && LineMailboxProtocol.IsTelemetryFresh(st.ReceivedUtc,DateTime.UtcNow,TimeSpan.FromSeconds(3)),","fresh")
    t=single(t,"            AutoContractCommissioned: LineAutoContractCommissioned,",
    "            AutoContractCommissioned: LineAutoContractCommissioned && LineMailboxProtocol.ProductionCommandSourceApproved && st?.MailboxSymbolsAvailable==true,","contract")
    t=single(t,"        OvStartReason.Text = (_lineMockMode ? \"TEST ONLY", "        OvStartReason.Text = (_lineMockMode ? \"TEST ONLY", "noop") if False else t
    t=single(t,"        OvStartReason.Foreground = DashBrush(evaluation.CanStart",
    """        if(!_lineMockMode && _dashStatus is { } blockedStatus)
        {
            if(!blockedStatus.MailboxSymbolsAvailable)
                OvStartReason.Text+=" | PLC HMI TAGS MISSING";
            else if(!blockedStatus.BeltSpeedMapReady)
                OvStartReason.Text+=" | BELT "+LineMailboxProtocol.DescribeSpeedMap(blockedStatus.BeltSpeedMapState);
            else if(!blockedStatus.HmiLinkFresh)
                OvStartReason.Text+=" | HMI HEARTBEAT STALE";
        }
        OvStartReason.Foreground = DashBrush(evaluation.CanStart""","reason")
    t=single(t,"        OvPresetHz.Text = hz;\n", "        OvPresetHz.Text = hz; // Estimated only, never sent to the MD200.\n","preview")
    t=single(t,'            T("CONVEYOR","MAIN.bMD200_Start",V(s.BeltRequest),"R/W*"),',
    '            T("CONVEYOR","MAIN.bMD200_Start",V(s.BeltRequest)),',"read-only-start")
    t=single(t,'            T("CONVEYOR","MAIN.rMD200_SetFrequency_Hz",$"{s.HzSet:F2} Hz","R/W*"),',
    '            T("CONVEYOR","MAIN.rMD200_SetFrequency_Hz",$"{s.HzSet:F2} Hz derived"),',"read-only-hz")
    t=single(t,'            T("CONVEYOR","MAIN.rMD200_Ramped_Hz",$"{s.HzActualReference:F2} Hz"),',
    """            T("CONVEYOR","MAIN.rMD200_Ramped_Hz",$"{s.HzActualReference:F2} Hz"),
            T("CONVEYOR","MAIN.rMD200_SetSpeed_mm_s",$"{s.RequestedSpeedMmS:F2} mm/s requested"),
            T("CONVEYOR","MAIN.rBeltAppliedSpeed_mm_s",$"{s.AppliedSpeedMmS:F2} mm/s accepted"),
            T("CONVEYOR","MAIN.bBeltSpeedMapReady",V(s.BeltSpeedMapReady)),
            T("CONVEYOR","MAIN.nBeltSpeedMapState",LineMailboxProtocol.DescribeSpeedMap(s.BeltSpeedMapState)),
            T("CONVEYOR","MAIN.nBeltCommandResult",s.BeltCommandResult.ToString()),
            T("HMI","MAIN.bHmiLinkFresh",V(s.HmiLinkFresh)),
            T("HMI","MAIN.nLineCommandResult",s.LineCommandResult.ToString()),
            T("HMI","MAIN.udiLineLastAckSeq",s.LineAckSequence.ToString()),
            T("HMI","MAIN.bLineAutoAccepted",V(s.LineAutoAccepted)),""","matrix")
    return t
rewrite(r"App\Vision\Dashboard.cs",dashboard)
def csproj(t):
    return single(t,'    <Compile Include="..\\Vision\\Vision\\LineInterlock.cs" Link="LineInterlock.cs" />',
     '    <Compile Include="..\\Vision\\Vision\\LineInterlock.cs" Link="LineInterlock.cs" />\n    <Compile Include="..\\Vision\\Vision\\LineMailboxProtocol.cs" Link="LineMailboxProtocol.cs" />',"csproj")
rewrite(r"App\Tests\Tests.csproj",csproj)
def tests(t):
    marker='Console.WriteLine("TOTAL_TEST_FAILURES="+failed);'
    insertion="""Check("mailbox: source commands are disarmed until hardware commissioning",
    !LineMailboxProtocol.ProductionCommandSourceApproved);
Check("mailbox: command sequence starts at one and wraps to one",
    LineMailboxProtocol.NextSequence(0)==1 && LineMailboxProtocol.NextSequence(uint.MaxValue)==1);
Check("mailbox: matching accepted ACK is not motion feedback",
    LineMailboxProtocol.Acknowledged(7,7,1) && !LineMailboxProtocol.Acknowledged(7,6,1)
    && !LineMailboxProtocol.Acknowledged(7,7,11) && !LineMailboxProtocol.Acknowledged(0,0,1));
Check("mailbox: no measured ratio means no Hz conversion",
    !LineMailboxProtocol.TryConvertSpeed(100,0,false,out _)
    && !LineMailboxProtocol.TryConvertSpeed(100,0,true,out _));
Check("mailbox: finite commissioned factor computes bounded Hz only",
    LineMailboxProtocol.TryConvertSpeed(100,20,true,out var hz100) && hz100==5
    && !LineMailboxProtocol.TryConvertSpeed(1000,20,true,out _)
    && !LineMailboxProtocol.TryConvertSpeed(double.NaN,20,true,out _));
Check("mailbox: stale/future timestamps are rejected",
    LineMailboxProtocol.IsTelemetryFresh(new DateTime(2026,10,22,10,0,0,DateTimeKind.Utc),
        new DateTime(2026,10,22,10,0,1,DateTimeKind.Utc),TimeSpan.FromSeconds(2))
    && !LineMailboxProtocol.IsTelemetryFresh(new DateTime(2026,10,22,10,0,0,DateTimeKind.Utc),
        new DateTime(2026,10,22,10,0,3,DateTimeKind.Utc),TimeSpan.FromSeconds(2)));
Check("mailbox: incomplete tag quality blocks full contract",
    !LineMailboxProtocol.AllReadbacksPresent(new System.Collections.Generic.Dictionary<string,bool>()));
Check("mailbox: block reason identifies missing PLC tags and command lock",
    LineMailboxProtocol.BlockReason(true,false,true,false,false,false).Contains("MISSING")
    && LineMailboxProtocol.BlockReason(true,true,true,false,true,true).Contains("NOT COMMISSIONED"));
"""
    return single(t,marker,insertion+marker,"tests")
rewrite(r"App\Tests\Program.cs",tests)
print("SYNC_STAGE_SOURCE_APPLIED")
