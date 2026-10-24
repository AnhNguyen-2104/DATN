using OpenCvSharp;
using PlateVision50.Vision;
using PlateVision50.Trigger;
using System;
using System.IO;
using System.Linq;
int failed=0;
void Check(string name,bool value){Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failed++;}

PlateResult Pose(int x,int y,int side=90)=>new(true,x,y,x,y,0,50.0/side,.98,[],[],[],"TEST");
var single=new MidlineTracker{Direction=BeltDirection.XPlus};
int n=0;
foreach(int x in new[]{100,220,340,380,410,450,480,520,550})
{
    var tracks=single.Update("video",new[]{Pose(x,260)},800,600,out var events);n+=events.Count;
    if(events.Count>0)Console.WriteLine("SINGLE CROSS x="+x+" id="+events[0].ProductId);
}
Check("single product crossing X+ emits once",n==1);
var reverse=new MidlineTracker{Direction=BeltDirection.XMinus};
n=0;
foreach(int x in new[]{710,650,540,430,395,360,250})
{reverse.Update("reverse",new[]{Pose(x,260)},800,600,out var events);n+=events.Count;}
Check("direction X- emits once",n==1);
var top=new MidlineTracker{Direction=BeltDirection.YPlus};
n=0;
foreach(int y in new[]{120,230,280,310,350,400})
{top.Update("vertical",new[]{Pose(250,y)},800,600,out var events);n+=events.Count;}
Check("direction Y+ emits once",n==1);
var bornAfter=new MidlineTracker{Direction=BeltDirection.XPlus};
n=0;
foreach(int x in new[]{450,500,550})
{bornAfter.Update("bornRight",new[]{Pose(x,100)},800,600,out var events);n+=events.Count;}
Check("plate first detected after trigger line must not trigger",n==0);
var multi=new MidlineTracker{Direction=BeltDirection.XPlus};
n=0;int distinct=0;var ids=new HashSet<int>();
foreach(int x in new[]{170,270,350,385,420,460,500})
{
    multi.Update("multi",new[]{Pose(x,150),Pose(x-30,440)},800,600,out var events);
    n+=events.Count;foreach(var e in events)ids.Add(e.ProductId);
}
Check("2 plates crossing 50% independently",n==2&&ids.Count==2);
var reset=new MidlineTracker{Direction=BeltDirection.XPlus};
reset.Update("s",new[]{Pose(399,210)},800,600,out _);
reset.Reset();
reset.Update("s",new[]{Pose(405,210)},800,600,out var suppress);
Check("reset does not trigger on appearing at right half",suppress.Count==0);
using(var blank=new Mat(new Size(800,600),MatType.CV_8UC3,new Scalar(220,220,220)))
{Check("empty frame does not detect",PlateDetector.AnalyzeAll(blank,new()).Count==0);}
foreach(double a in new[]{0.0,15.0,-30.0,35.5,44.0})
{
    using var img=new Mat(new Size(800,600),MatType.CV_8UC3,new Scalar(222,222,222));
    using var tmp=img.Clone();
    Cv2.Rectangle(tmp,new Rect(270,170,260,260),new Scalar(55,55,55),-1);
    foreach(var point in new[]{new Point(296,196),new Point(504,196),new Point(504,404),new Point(296,404)})
        Cv2.Circle(tmp,point,13,new Scalar(170,170,170),-1);
    using var rot=Cv2.GetRotationMatrix2D(new Point2f(400,300),a,1);
    Cv2.WarpAffine(tmp,img,rot,new Size(800,600),InterpolationFlags.Linear,BorderTypes.Constant,new Scalar(222,222,222));
    var r=PlateDetector.Analyze(img,new());
    double err=r.AngleDeg-a;
    while(err>=45)err-=90;while(err< -45)err+=90;
    Check("rotated geometry angle "+a,r.Found&&Math.Abs(err)<2.5&&Math.Abs(r.Xmm)<1&&Math.Abs(r.Ymm)<1&&r.GlueMm.Length==4);
}
var bp=BeltCoordinates.Map(720,520,35.5,1440,1080,.20,double.NaN,double.NaN,0,85,BeltDirection.YMinus);
Check("conveyor Y-up maps image progress to robot X positive",Math.Abs(bp.Xmm-4)<.0001);
Check("camera midpoint maps robot X=0 and middle lateral Y=85",Math.Abs(BeltCoordinates.Map(720,540,0,1440,1080,.2,double.NaN,double.NaN,0,85,BeltDirection.YMinus).Xmm)<.0001&&Math.Abs(bp.Ymm-85)<.0001);
// Check that screen-space caption groups are laid OUTSIDE the workpieces.
PlateResult Box(int x,int y,int side=180)
{
    var corners=new [] {
        new Point2f(x-side/2,y-side/2),
        new Point2f(x+side/2,y-side/2),
        new Point2f(x+side/2,y+side/2),
        new Point2f(x-side/2,y+side/2) };
    return new PlateResult(true,x,y,0,0,32,0.2,.98,
        new [] {new DetectionPoint(x-30,y-30),new DetectionPoint(x+30,y-30),
                 new DetectionPoint(x+30,y+30),new DetectionPoint(x-30,y+30)},
        [],corners,"OK");
}
var labelsSource=new [] {Box(400,440),Box(990,820)};
var groups=VisionLabelOverlay.Layout(labelsSource,1440,1080,1050,740);
Check("WPF: two plates have independent caption groups",groups.Count==2);
Check("WPF: caption groups do not overlap each other",
      groups.Count==2 && !VisionLabelOverlay.Overlap(groups[0],groups[1],5));
double factor=Math.Min(1050.0/1440,740.0/1080);
double offx=(1050-1440*factor)/2,offy=(740-1080*factor)/2;
var projected=labelsSource.Select((p,i)=>new VisionLabelOverlay.LabelRect(
    offx+p.OutlinePx.Min(v=>v.X)*factor,offy+p.OutlinePx.Min(v=>v.Y)*factor,
    (p.OutlinePx.Max(v=>v.X)-p.OutlinePx.Min(v=>v.X))*factor,
    (p.OutlinePx.Max(v=>v.Y)-p.OutlinePx.Min(v=>v.Y))*factor,i)).ToArray();
Check("WPF: callout group 1 stays outside both workpieces",
  !projected.Any(b=>VisionLabelOverlay.Overlap(groups[0],b,5)));
Check("WPF: callout group 2 stays outside both workpieces",
  !projected.Any(b=>VisionLabelOverlay.Overlap(groups[1],b,5)));
Check("WPF: camera labels fit viewport",
  groups.All(v=>v.X>=0&&v.Y>=0&&v.X+v.W<=1050&&v.Y+v.H<=740));
// Same physical plate must retain its ID and its caption when detection order flips.
var stable=new MidlineTracker{Direction=BeltDirection.YMinus};
var stableA=stable.Update("source",new[]{Pose(390,230),Pose(690,650)},1440,1080,out _);
int stableIdA=stableA[0].ProductId,stableIdB=stableA[1].ProductId;
bool stableAssignments=true;
for(int step=1;step<25;step++)
{
    int aY=230-step*4,bY=650-step*4;
    PlateResult[] input=step%2==0
        ?new[]{Pose(390,aY),Pose(690,bY)}
        :new[]{Pose(690,bY),Pose(390,aY)};
    var results=stable.Update("source",input,1440,1080,out _);
    for(int k=0;k<results.Count;k++)
    {
        int expect=(Math.Abs(input[k].CenterPxX-390)<1)?stableIdA:stableIdB;
        if(results[k].ProductId!=expect)stableAssignments=false;
    }
}
Check("Track ID keeps belonging to its plate if detection order swaps each frame",stableAssignments);
var persistent=new VisionLabelOverlay.LabelPlacementTracker();
var beforePlates=new []{Box(400,440),Box(990,820)};
var beforeMap=new System.Collections.Generic.Dictionary<int,int>{{0,101},{1,202}};
var beforeGroups=persistent.LayoutTracked(beforePlates,beforeMap,"DEMO",1440,1080,1050,740);
var nextPlates=new []{Box(990,801),Box(400,415)}; // order reverses; both move up
var nextMap=new System.Collections.Generic.Dictionary<int,int>{{0,202},{1,101}};
var afterGroups=persistent.LayoutTracked(nextPlates,nextMap,"DEMO",1440,1080,1050,740);
var aBefore=beforeGroups.First(v=>v.Index==0); var aAfter=afterGroups.First(v=>v.Index==1);
var bBefore=beforeGroups.First(v=>v.Index==1); var bAfter=afterGroups.First(v=>v.Index==0);
Check("Label for Track #101 follows same plate after list reorder",
    aAfter.Y<aBefore.Y && Math.Abs(aAfter.X-aBefore.X)<12);
Check("Label for Track #202 follows its own plate after list reorder",
    bAfter.Y<bBefore.Y && Math.Abs(bAfter.X-bBefore.X)<12);
Check("Each label moves smoothly instead of jumping across frame",
    Math.Abs(aAfter.Y-aBefore.Y)<40&&Math.Abs(bAfter.Y-bBefore.Y)<40);
bool noOverlap=afterGroups.Count==2&&!VisionLabelOverlay.Overlap(afterGroups[0],afterGroups[1],6);
Check("Labels stay disjoint with persistent identity",noOverlap);
persistent.Reset();
var replay=persistent.LayoutTracked(beforePlates,beforeMap,"DEMO",1440,1080,1050,740);
Check("Replay resets label layout state",replay.Count==2 &&
    Math.Abs(replay.First(v=>v.Index==0).Y-aBefore.Y)<.001);
// If the midline crosses a moving object, captions must be offset away
// from the trigger line whenever an uncluttered placement exists.
var splitScreenLabels=new VisionLabelOverlay.LabelPlacementTracker();
var sampleAtLine=new[]{Box(720,540)};
var lineMap=new System.Collections.Generic.Dictionary<int,int>{{0,17}};
var offLine=splitScreenLabels.LayoutTracked(sampleAtLine,lineMap,"VIDEO",
    1440,1080,1050,740,.5,true);
double yLine=740.0/2;
Check("WPF text label avoids horizontal trigger line if space is available",
    offLine.Count==1&&(offLine[0].Y+offLine[0].H+3<yLine || offLine[0].Y-3>yLine));
Console.WriteLine("VIDEO SAMPLES");
for(int file=1;file<=4;file++)
{
    using var vid=new VideoCapture(@"C:\Users\aml\Desktop\DEMO\"+file+".avi");
    if(!vid.IsOpened()){failed++;Console.WriteLine("NO VIDEO: "+file);continue;}
    int present=0,multiple=0;
    for(int j=0;j<12;j++)
    {
        vid.Set(VideoCaptureProperties.PosFrames,(int)(vid.FrameCount*(j+0.2)/12));
        using var frame=new Mat();
        if(!vid.Read(frame)||frame.Empty())continue;
        var detections=PlateDetector.AnalyzeAll(frame,new());
        if(detections.Count>0)present++;
        if(detections.Count>1)multiple++;
    }
    Console.WriteLine($"VIDEO {file}: detections {present}/12, multi {multiple}/12 (not ground truth)");
}
Console.WriteLine("VIDEO MIDLINE CROSSING EXPERIMENT - VIDEO 1 FIRST 1000 FRAMES");
using(var cap=new VideoCapture(@"C:\Users\aml\Desktop\DEMO\1.avi"))
{
    var trackers=Enum.GetValues<BeltDirection>().ToDictionary(dir=>dir,dir=>new MidlineTracker{Direction=dir});
    var counters=Enum.GetValues<BeltDirection>().ToDictionary(dir=>dir,dir=>0);
    int recognized=0;
    for(int frameIdx=0;frameIdx<1000;frameIdx++)
    {
        using var frame=new Mat();
        if(!cap.Read(frame)||frame.Empty())break;
        if(frameIdx%3!=0)continue;
        var parts=PlateDetector.AnalyzeAll(frame,new());
        if(parts.Count>0)recognized++;
        foreach(var direction in Enum.GetValues<BeltDirection>())
        {
            trackers[direction].Update("video1",parts,frame.Width,frame.Height,out var ev);
            counters[direction]+=ev.Count;
        }
    }
    Console.WriteLine("VIDEO1_DETECTED_SAMPLES="+recognized);
    foreach(var direction in counters.Keys)Console.WriteLine("VIDEO1_CROSS_"+direction+"="+counters[direction]);
}
// The same production code used by the WPF offline pipeline is tested here.
Console.WriteLine("CAMERA → FIFO CONTRACT SIM TESTS (NO ADS WRITE):");
var sim=new PlateVision50.TwinCAT.CameraPipelineSimulator(200,0,85,0,40,40);
var ack=sim.Accept("HIK/169.254.113.1/1",0,0,0,100);
Check("simulation accepted one coordinate record",ack.ProductId==1&&ack.QueueCount==1);
Check("TwinCAT FB +20/+20 corner",Math.Abs(ack.Corners[0].X+180)<1e-9&&Math.Abs(ack.Corners[0].Y-105)<1e-9);
Check("TwinCAT FB -20/+20 corner",Math.Abs(ack.Corners[1].X+220)<1e-9&&Math.Abs(ack.Corners[1].Y-105)<1e-9);
Check("TwinCAT FB -20/-20 corner",Math.Abs(ack.Corners[2].X+220)<1e-9&&Math.Abs(ack.Corners[2].Y-65)<1e-9);
Check("TwinCAT FB +20/-20 corner",Math.Abs(ack.Corners[3].X+180)<1e-9&&Math.Abs(ack.Corners[3].Y-65)<1e-9);
var moved=sim.GetHead(125);
Check("encoder +25 follows FIFO head X and 4 corners",moved!=null&&Math.Abs(moved.RobotX+175)<1e-9
  &&moved.Corners.Zip(ack.Corners).All(z=>Math.Abs(z.First.X-z.Second.X-25)<1e-8&&z.First.Y==z.Second.Y));
bool duplicate=false;try{sim.Accept("HIK/169.254.113.1/1",0,0,0,100);}catch(InvalidOperationException){duplicate=true;}
Check("duplicate track key rejected",duplicate&&sim.Accepted==1);
var rotated=new PlateVision50.TwinCAT.CameraPipelineSimulator(200,0,85,90,40,40)
 .Accept("TEST/ROTATED",10,0,0,0);
Check("yaw transform maps camera X to robot Y",Math.Abs(rotated.RobotX+200)<1e-7&&Math.Abs(rotated.RobotY-95)<1e-7);
var theta=new PlateVision50.TwinCAT.CameraPipelineSimulator().Accept("TEST/THETA",0,0,45,0);
Check("rotated square corners maintain 40mm pitch",
  Math.Abs(Math.Sqrt(Math.Pow(theta.Corners[0].X-theta.Corners[1].X,2)+
  Math.Pow(theta.Corners[0].Y-theta.Corners[1].Y,2))-40)<1e-7);
for(int i=1;i<8;i++)sim.Accept("TEST/QUEUE/"+i,0,0,0,i);
bool full=false;try{sim.Accept("TEST/OVERFLOW",0,0,0,9);}catch(InvalidOperationException){full=true;}
Check("queue limit 8 with refusal not overwrite",sim.QueueCount==8&&full&&sim.Accepted==8);
sim.CompleteHead();
Check("FIFO completion preserves order",sim.QueueCount==7&&sim.GetHead(0)?.ProductId==2);
sim.Reset();
Check("reset clears queue and track identity",sim.QueueCount==0&&sim.Accepted==0);
bool nan=false;try{sim.Accept("TEST/NAN",double.NaN,0,0,0);}catch(ArgumentException){nan=true;}
Check("nonfinite vision coordinates never accepted",nan);
var dryRun=new PlateVision50.TwinCAT.CameraPipelineSimulator();
int dryEvents=0;
using(var testVideo=new VideoCapture(@"C:\Users\aml\Desktop\DEMO\1.avi"))
{
    var midline=new MidlineTracker{Direction=BeltDirection.YMinus};
    var settings=new VisionOptions();
    for(int f=0;f<1000;f++)
    {
        using var frame=new Mat();
        if(!testVideo.Read(frame)||frame.Empty())break;
        if(f%3!=0)continue;
        var detections=PlateDetector.AnalyzeAll(frame,settings);
        midline.Update("SIM_VIDEO_1",detections,frame.Width,frame.Height,out var events);
        foreach(var e in events)
        {
            var pose=BeltCoordinates.FromDetection(e.Pose,settings,frame.Width,frame.Height,BeltDirection.YMinus);
            // emulate consuming one product at robot station so FIFO doesn't overflow
            var received=dryRun.Accept($"{e.Source}/{e.ProductId}",pose.Xmm,pose.Ymm,pose.ThetaDeg,0);
            Check("video → simulated FIFO ACK " + received.ProductId,received.Corners.Length==4);
            dryEvents++;
            dryRun.CompleteHead();
        }
    }
}
Check("recorded multi-product video crosses -> FIFO mock end-to-end",dryEvents>=5&&dryRun.Accepted==(uint)dryEvents);
// Safety regression: the legacy unsupervised ADS write path MUST never reach
// PLC symbols, including when an operator bypasses a hidden WPF control.
bool unguardedWriteRefused=false;
try {new PlateVision50.TwinCAT.AdsPreview().WritePose(0,0,0);}
catch(NotSupportedException){unguardedWriteRefused=true;}
Check("legacy direct ADS pose write is fail-closed without PLC access",unguardedWriteRefused);
Console.WriteLine("READ ONLY ADS FIFO SMOKE:");
try
{
    var s=new PlateVision50.TwinCAT.AdsFifoBenchBridge().ReadStatus();
    Console.WriteLine($"ADS QUEUE={s.Count}, HEAD_ID={s.HeadProduct}, SPEED={s.EncoderMmPerSec:F2}, MOTION_SAFE={s.SafeMode}, POINTS={s.GluePoints.Length}");
    Check("PLC FIFO read-only ADS 4 glue points",s.GluePoints.Length==4);
}
catch(Exception ex){Console.WriteLine("ADS READ FAIL "+ex);failed++;}
// Line interlock: every line command fails closed unless the ADS snapshot,
// safety circuit, camera, speed setpoint and AUTO contract all check out.
Console.WriteLine("LINE INTERLOCK / AUTO CONTRACT FAIL-CLOSED TESTS:");
InterlockInputs ReadyInputs()=>new(true,true,false,true,true,true,false,false,120,true,true);
bool AcceptedSpeed(string text,double expect)=>LineSpeedPreset.TryParse(text,out var value,out var why)&&Math.Abs(value-expect)<1e-9&&why.Length==0;
bool RejectedSpeed(string text)=>!LineSpeedPreset.TryParse(text,out _,out var why)&&why.Length>0;
bool Detail(InterlockEvaluation evaluation,InterlockCheckId id,string fragment)=>
    evaluation.Checks.Single(c=>c.Id==id).Detail.Contains(fragment,StringComparison.OrdinalIgnoreCase);

Check("interlock: speed 120 parses",AcceptedSpeed("120",120.0));
Check("interlock: speed bounds accepted",AcceptedSpeed("1",1.0)&&AcceptedSpeed("150",150.0));
Check("interlock: blank speed rejected",RejectedSpeed("")&&RejectedSpeed("   "));
Check("interlock: non-numeric speed rejected",RejectedSpeed("abc"));
Check("interlock: speed 0 rejected",RejectedSpeed("0"));
Check("interlock: speed 151 rejected",RejectedSpeed("151"));
Check("interlock: NaN speed rejected",RejectedSpeed("NaN"));
Check("interlock: Hz not calculated without verified calibration or zero factor",
    LineSpeedPreset.DerivedHzInfo(120,false,50).Contains("NOT CALCULATED")&&
    LineSpeedPreset.DerivedHzInfo(120,true,0).Contains("NOT CALCULATED"));
Check("interlock: Hz preview marked calculated not measured",
    LineSpeedPreset.DerivedHzInfo(120,true,50).Contains("2.40 Hz")&&
    LineSpeedPreset.DerivedHzInfo(120,true,50).Contains("not measured"));

var ilModel=new LineInterlockModel();
var ilReady=ilModel.Evaluate(ReadyInputs());
Check("interlock: complete snapshot is READY",
    ilReady.State==InterlockState.Ready&&ilReady.CanStart&&ilReady.CanStop&&!ilReady.CanReset&&
    ilReady.PrimaryReason.Contains("READY TO REQUEST"));
Check("interlock: exactly 8 checks in contract order",
    ilReady.Checks.Count==8&&ilReady.Checks.Select(c=>c.Id).SequenceEqual(new[]{
        InterlockCheckId.AdsLink,InterlockCheckId.Estop,InterlockCheckId.PlcQ,InterlockCheckId.GantryFault,
        InterlockCheckId.CameraFrame,InterlockCheckId.SpeedSetpoint,InterlockCheckId.AutoContract,InterlockCheckId.ScaraExcluded}));

var ilOffline=new LineInterlockModel();
var ilOffEval=ilOffline.Evaluate(ReadyInputs() with {AdsOnline=false});
Check("interlock: ADS offline blocks start, stop and reset",
    ilOffEval.State==InterlockState.Offline&&!ilOffEval.CanStart&&!ilOffEval.CanStop&&!ilOffEval.CanReset&&
    ilOffEval.PrimaryReason.Contains("ADS 851 OFFLINE")&&ilOffEval.Checks.Count==8);
Check("interlock: offline START refused",
    !ilOffline.RequestStart(ReadyInputs() with {AdsOnline=false}).accepted&&ilOffline.State==InterlockState.Offline);
var ilOffStop=ilOffline.RequestStop(ReadyInputs() with {AdsOnline=false});
Check("interlock: offline STOP refused with physical E-STOP advice",
    !ilOffStop.accepted&&ilOffStop.message.Contains("physical E-STOP"));
Check("interlock: offline RESET refused",
    !ilOffline.RequestReset(ReadyInputs() with {AdsOnline=false}).accepted);

var ilSafety=new LineInterlockModel();
var ilEstop=ilSafety.Evaluate(ReadyInputs() with {EstopOk=false});
Check("interlock: E-stop not OK blocks start",
    ilEstop.State==InterlockState.NotReady&&!ilEstop.CanStart&&
    Detail(ilEstop,InterlockCheckId.Estop,"Emergency stop")&&
    ilEstop.PrimaryReason==ilEstop.Checks.First(c=>!c.Ok).Detail);
var ilGlobal=ilSafety.Evaluate(ReadyInputs() with {GlobalStop=true});
Check("interlock: global stop blocks start",
    !ilGlobal.CanStart&&Detail(ilGlobal,InterlockCheckId.Estop,"Global stop"));
var ilPlcq=ilSafety.Evaluate(ReadyInputs() with {PlcQConnected=false});
Check("interlock: PLC-Q disconnect blocks start",
    !ilPlcq.CanStart&&Detail(ilPlcq,InterlockCheckId.PlcQ,"disconnected"));
var ilGantry=ilSafety.Evaluate(ReadyInputs() with {GantryFault=true,GantryBusy=true});
Check("interlock: gantry fault blocks start and reports cycle context",
    !ilGantry.CanStart&&Detail(ilGantry,InterlockCheckId.GantryFault,"cycle in progress"));
var ilBusy=ilSafety.Evaluate(ReadyInputs() with {GantryBusy=true});
Check("interlock: busy gantry cycle alone does not block start",
    ilBusy.CanStart&&Detail(ilBusy,InterlockCheckId.GantryFault,"cycle in progress"));
var ilStale=ilSafety.Evaluate(ReadyInputs() with {CameraFrameFresh=false});
Check("interlock: stale camera frame blocks start",
    !ilStale.CanStart&&Detail(ilStale,InterlockCheckId.CameraFrame,"stale"));
var ilNoCamera=ilSafety.Evaluate(ReadyInputs() with {CameraConnected=false});
Check("interlock: missing camera blocks start",
    !ilNoCamera.CanStart&&Detail(ilNoCamera,InterlockCheckId.CameraFrame,"No camera"));
var ilSlow=ilSafety.Evaluate(ReadyInputs() with {PresetSpeedMmS=0});
var ilFast=ilSafety.Evaluate(ReadyInputs() with {PresetSpeedMmS=151});
var ilNanSpeed=ilSafety.Evaluate(ReadyInputs() with {PresetSpeedMmS=double.NaN});
Check("interlock: out-of-range speed blocks start",
    !ilSlow.CanStart&&!ilFast.CanStart&&
    Detail(ilSlow,InterlockCheckId.SpeedSetpoint,"below minimum")&&
    Detail(ilFast,InterlockCheckId.SpeedSetpoint,"maximum"));
Check("interlock: non-finite speed blocks start",
    !ilNanSpeed.CanStart&&Detail(ilNanSpeed,InterlockCheckId.SpeedSetpoint,"not a valid number"));
var ilScara=ilSafety.Evaluate(ReadyInputs() with {ScaraExcluded=false});
Check("interlock: SCARA station not excluded blocks Gantry-only AUTO",
    !ilScara.CanStart&&Detail(ilScara,InterlockCheckId.ScaraExcluded,"Station 02"));

var ilContract=new LineInterlockModel();
var ilNoContract=ilContract.Evaluate(ReadyInputs() with {AutoContractCommissioned=false});
Check("interlock: uncommissioned AUTO mailbox blocks production start",
    ilNoContract.State==InterlockState.NotReady&&
    Detail(ilNoContract,InterlockCheckId.AutoContract,"not commissioned")&&
    !ilContract.RequestStart(ReadyInputs() with {AutoContractCommissioned=false}).accepted);
ilContract.MockMode=true;
var ilMock=ilContract.Evaluate(ReadyInputs() with {AutoContractCommissioned=false});
Check("interlock: mock mode makes contract READY for offline demo",
    ilMock.State==InterlockState.Ready&&ilMock.CanStart&&
    Detail(ilMock,InterlockCheckId.AutoContract,"TEST ONLY"));
var ilMockStart=ilContract.RequestStart(ReadyInputs() with {AutoContractCommissioned=false});
Check("interlock: mock START accepted and names TEST ONLY no write",
    ilMockStart.accepted&&ilContract.State==InterlockState.StartRequested&&
    ilMockStart.message.Contains("TEST ONLY - NO PLC WRITE")&&
    ilMockStart.message.Contains("Tracking"));
ilContract.AcknowledgeStart();
Check("interlock: START acknowledged -> RunningAcknowledged",
    ilContract.State==InterlockState.RunningAcknowledged);
var ilEarlyReset=ilContract.RequestReset(ReadyInputs() with {AutoContractCommissioned=false});
Check("interlock: RESET refused while running",
    !ilEarlyReset.accepted&&ilContract.State==InterlockState.RunningAcknowledged&&
    ilEarlyReset.message.Contains("RESET allowed only after"));
var ilStop=ilContract.RequestStop(ReadyInputs() with {AutoContractCommissioned=false});
Check("interlock: STOP accepted when online and not a certified E-stop",
    ilStop.accepted&&ilContract.State==InterlockState.StopRequested&&
    ilStop.message.Contains("not a certified emergency stop"));
var ilReset=ilContract.RequestReset(ReadyInputs() with {AutoContractCommissioned=false});
Check("interlock: RESET accepted after STOP",
    ilReset.accepted&&ilContract.State==InterlockState.NotReady);
ilContract.RequestStop(ReadyInputs() with {AutoContractCommissioned=false});
ilContract.AcknowledgeStop();
Check("interlock: STOP acknowledged -> NotReady",ilContract.State==InterlockState.NotReady);
var ilReadyAgain=ilContract.Evaluate(ReadyInputs());
var ilFirstStart=ilContract.RequestStart(ReadyInputs());
var ilSecondStart=ilContract.RequestStart(ReadyInputs());
Check("interlock: START accepted only from READY",
    ilReadyAgain.State==InterlockState.Ready&&ilFirstStart.accepted&&!ilSecondStart.accepted&&
    ilContract.State==InterlockState.StartRequested);
var ilFault=new LineInterlockModel();
ilFault.RequestStart(ReadyInputs());
ilFault.AcknowledgeStart();
var ilFaulted=ilFault.Evaluate(ReadyInputs() with {GantryFault=true});
Check("interlock: safety loss while running latches FAULT and allows reset",
    ilFaulted.State==InterlockState.Fault&&ilFaulted.CanReset&&!ilFaulted.CanStart&&
    ilFault.RequestReset(ReadyInputs() with {GantryFault=true}).accepted&&
    ilFault.State==InterlockState.NotReady);
// TEST ONLY mock mode must demonstrate the whole lifecycle with no field signals.
var ilMockDemo=new LineInterlockModel{MockMode=true};
var ilNoField=new InterlockInputs(false,false,false,false,false,false,true,false,0,false,false);
var ilMockAllFalse=ilMockDemo.Evaluate(ilNoField);
Check("interlock: mock all-false inputs are READY and startable",
    ilMockAllFalse.State==InterlockState.Ready&&ilMockAllFalse.CanStart&&ilMockAllFalse.CanStop&&
    ilMockAllFalse.PrimaryReason.Contains("TEST ONLY"));
Check("interlock: mock reports all 8 checks OK with TEST ONLY SIMULATION details",
    ilMockAllFalse.Checks.Count==8&&ilMockAllFalse.Checks.All(c=>c.Ok)&&
    ilMockAllFalse.Checks.All(c=>c.Detail.StartsWith("TEST ONLY SIMULATION"))&&
    ilMockAllFalse.Checks.All(c=>c.Detail.Contains("no PLC/field signal")));
var ilMockDemoStart=ilMockDemo.RequestStart(ilNoField);
Check("interlock: mock START accepted with no ADS and names TEST ONLY no write",
    ilMockDemoStart.accepted&&ilMockDemo.State==InterlockState.StartRequested&&
    ilMockDemoStart.message.Contains("TEST ONLY - NO PLC WRITE"));
ilMockDemo.AcknowledgeStart();
Check("interlock: mock START acknowledged -> RunningAcknowledged",
    ilMockDemo.State==InterlockState.RunningAcknowledged);
var ilMockDemoStop=ilMockDemo.RequestStop(ilNoField);
Check("interlock: mock STOP accepted from running without ADS",
    ilMockDemoStop.accepted&&ilMockDemo.State==InterlockState.StopRequested);
var ilMockDemoReset=ilMockDemo.RequestReset(ilNoField);
Check("interlock: mock RESET accepted after STOP -> NotReady",
    ilMockDemoReset.accepted&&ilMockDemo.State==InterlockState.NotReady);

var ilProductionOff=new LineInterlockModel();
var ilProductionEval=ilProductionOff.Evaluate(ilNoField);
Check("interlock: production offline fail-closed regression",
    ilProductionEval.State==InterlockState.Offline&&!ilProductionEval.CanStart&&
    !ilProductionEval.CanStop&&!ilProductionEval.CanReset&&
    ilProductionEval.PrimaryReason.Contains("ADS 851 OFFLINE"));
var ilMockOff=new LineInterlockModel{MockMode=true};
ilMockOff.Evaluate(ReadyInputs());
ilMockOff.MockMode=false;
var ilRestoredOffline=ilMockOff.Evaluate(ilNoField);
Check("interlock: mock off restores production Offline fail-closed rules",
    ilRestoredOffline.State==InterlockState.Offline&&!ilRestoredOffline.CanStart&&
    ilRestoredOffline.Checks.Any(c=>!c.Ok));
var ilRestoredNotReady=ilMockOff.Evaluate(ReadyInputs() with {EstopOk=false});
Check("interlock: mock off restores production NotReady on failed check",
    ilRestoredNotReady.State==InterlockState.NotReady&&!ilRestoredNotReady.CanStart&&
    ilRestoredNotReady.PrimaryReason.Contains("Emergency stop"));
Console.WriteLine("TOTAL_TEST_FAILURES="+failed);
return failed==0?0:1;
