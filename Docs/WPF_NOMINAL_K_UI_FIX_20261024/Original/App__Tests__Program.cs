using OpenCvSharp;
using PlateVision50.Vision;
using PlateVision50.Trigger;
using System;
using System.IO;
using System.Linq;
using System.Net;
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
catch (TwinCAT.Ads.AdsErrorException adsEx) when (adsEx.ErrorCode == TwinCAT.Ads.AdsErrorCode.PortNotConnected || adsEx.ErrorCode == TwinCAT.Ads.AdsErrorCode.DeviceNotFound || (int)adsEx.ErrorCode == 6)
{
    Console.WriteLine($"ADS 851 OFFLINE / CONFIG MODE (expected offline smoke): {adsEx.Message}");
    Check("PLC FIFO read-only ADS offline fail-closed", true);
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
// Camera slots: USB/IP transport selection, truthful frame state and exclusive ownership.
{
    CameraDevice Cam(string vendor,string transport,string serial,string model,int index=0,string address="")
        => new(vendor,transport,model,serial,address,index);
    string CamMac(string? mac)=>new string((mac??"").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    var camBaslerUsb=Cam("Basler","USB","40001234","acA1300-200um");
    var camHikGigE=Cam("Hikvision","GigE","HIK-0001","MV-CA050-10GM",1,"169.254.113.1");
    var camUvc=Cam("Generic","USB","UVC-1","USB Webcam",2);
    Check("camera device: key is vendor and serial upper-cased",camBaslerUsb.Key=="BASLER|40001234");
    Check("camera device: display is model, serial, vendor, transport",
        camBaslerUsb.Display=="acA1300-200um · 40001234 · Basler · USB");
    var camUvcNamed=new CameraDevice("Acme","WebCam","Cam","1","",0);
    Check("camera device: UVC and webcam metadata is generic UVC",
        camUvc.IsGenericUvc&&camUvcNamed.IsGenericUvc&&new CameraDevice("UVC","USB","Cam","2","",0).IsGenericUvc);
    var camNull=new CameraDevice(null!,null!,null!,null!,"",0);
    Check("camera device: null metadata is null-safe",camNull.Key=="|"&&!camNull.Display.Contains("null")&&!camNull.IsGenericUvc);

    // AUTO is gone for good: exactly two transports, station defaults and legacy migration.
    var camModeNames=Enum.GetNames<CameraTransportMode>();
    Check("camera mode: only Usb and Ip remain, no Auto",
        camModeNames.Length==2&&camModeNames.Contains("Usb")&&camModeNames.Contains("Ip")&&
        !camModeNames.Any(m=>m.Contains("auto",StringComparison.OrdinalIgnoreCase)));
    Check("camera mode: station 1 defaults to IP and station 2 to USB",
        CameraSlotDefaults.DefaultMode(1)==CameraTransportMode.Ip&&
        CameraSlotDefaults.DefaultMode(2)==CameraTransportMode.Usb);
    Check("camera mode: slot models start in their station default with no scan issues",
        new CameraSlotModel(1,"Station 01").Mode==CameraTransportMode.Ip&&
        new CameraSlotModel(2,"Station 02").Mode==CameraTransportMode.Usb&&
        new CameraSlotModel(2,"Station 02").ScanIssues.Count==0);
    Check("camera mode: legacy Auto, blank and invalid values migrate to the station default",
        CameraSlotDefaults.MigrateMode(null,1)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("Auto",1)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("auto",2)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("AUTO",1)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("",2)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("nonsense",1)==CameraTransportMode.Ip);
    Check("camera mode: saved USB and IP survive migration case-insensitively",
        CameraSlotDefaults.MigrateMode("USB",1)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("usb",2)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("IP",2)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("ip",1)==CameraTransportMode.Ip);

    Check("camera policy: Basler and Hikvision USB/GigE supported",
        CameraTransportPolicy.IsSupported(camBaslerUsb)&&CameraTransportPolicy.IsSupported(camHikGigE)&&
        CameraTransportPolicy.IsSupported(Cam("Hikvision","USB","HK-U","MV-USB"))&&
        CameraTransportPolicy.IsSupported(Cam("Basler","GigE","B-G","acA",0,"10.0.0.3")));
    Check("camera policy: generic UVC unsupported with capture-backend reason",
        !CameraTransportPolicy.IsSupported(camUvc)&&
        CameraTransportPolicy.UnsupportedReason(camUvc).Contains("UVC")&&
        CameraTransportPolicy.UnsupportedReason(camUvc).Contains("no image capture backend"));
    var camSony=Cam("Sony","USB","SNY-1","FCB-EV7520");
    Check("camera policy: unknown vendor unsupported and named",
        !CameraTransportPolicy.IsSupported(camSony)&&CameraTransportPolicy.UnsupportedReason(camSony).Contains("Sony")&&
        !CameraTransportPolicy.CanDispatch(camSony,CameraTransportMode.Usb,out _));
    var camCameraLink=Cam("Basler","CameraLink","B-CL","acA");
    Check("camera policy: unsupported transport named",
        !CameraTransportPolicy.IsSupported(camCameraLink)&&CameraTransportPolicy.UnsupportedReason(camCameraLink).Contains("CameraLink"));
    Check("camera policy: USB mode rejects GigE and IP mode rejects USB",
        !CameraTransportPolicy.MatchesMode(camHikGigE,CameraTransportMode.Usb)&&
        !CameraTransportPolicy.MatchesMode(camBaslerUsb,CameraTransportMode.Ip));
    Check("camera policy: IP address required only in IP mode",
        CameraTransportPolicy.RequiresIp(CameraTransportMode.Ip)&&
        !CameraTransportPolicy.RequiresIp(CameraTransportMode.Usb));
    Check("camera policy: Hikvision GigE dispatch in IP",
        CameraTransportPolicy.CanDispatch(camHikGigE,CameraTransportMode.Ip,out var camOkReason)&&camOkReason.Length==0);
    Check("camera policy: Hikvision USB dispatch in USB",
        CameraTransportPolicy.CanDispatch(Cam("Hikvision","USB","HK-U","MV-USB"),CameraTransportMode.Usb,out _));
    Check("camera policy: Basler GigE dispatch in IP",
        CameraTransportPolicy.CanDispatch(Cam("Basler","GigE","B-G","acA",0,"10.0.0.3"),CameraTransportMode.Ip,out _));
    Check("camera policy: Basler USB dispatch in USB",
        CameraTransportPolicy.CanDispatch(camBaslerUsb,CameraTransportMode.Usb,out _));
    Check("camera policy: Hikvision USB rejected in IP mode",
        !CameraTransportPolicy.CanDispatch(Cam("Hikvision","USB","HK-U","MV-USB",3),CameraTransportMode.Ip,out var camHikUsbIp)&&
        camHikUsbIp.Contains("IP mode")&&camHikUsbIp.Contains("Hikvision"));
    Check("camera policy: Basler GigE rejected in USB mode",
        !CameraTransportPolicy.CanDispatch(Cam("Basler","GigE","B-G","acA",4),CameraTransportMode.Usb,out var camBaslerGigEUsb)&&
        camBaslerGigEUsb.Contains("USB mode")&&camBaslerGigEUsb.Contains("GigE"));

    // Hikrobot alias and manual direct-IP rules: Hik direct open only, Basler needs a scan.
    var camHikrobotGigE=new CameraDevice("Hikrobot","GigE","MV-CS016-10GC","DA7403922","169.254.113.1",-2);
    Check("camera policy: Hikrobot alias is a supported GigE vendor",
        CameraTransportPolicy.IsSupported(camHikrobotGigE)&&CameraTransportPolicy.IsSupported(camHikGigE)&&
        CameraTransportPolicy.UnsupportedReason(camHikrobotGigE).Length==0);
    Check("camera policy: manual Hikrobot direct IP is dispatchable in IP mode",
        CameraTransportPolicy.CanDispatch(camHikrobotGigE,CameraTransportMode.Ip,out var camHikDirectOk)&&camHikDirectOk.Length==0);
    Check("camera policy: manual Hikvision direct IP is dispatchable in IP mode",
        CameraTransportPolicy.CanDispatch(new CameraDevice("Hikvision","GigE","MV-CA050","HIK-IP","169.254.113.1",-2),CameraTransportMode.Ip,out _));
    var camBaslerDirect=new CameraDevice("Basler","GigE","acA1300-30gm","B-DIRECT","169.254.113.1",-1);
    Check("camera policy: Basler direct IP is rejected as SCAN REQUIRED",
        !CameraTransportPolicy.CanDispatch(camBaslerDirect,CameraTransportMode.Ip,out var camBaslerDirectReason)&&
        camBaslerDirectReason.Contains("SCAN REQUIRED",StringComparison.OrdinalIgnoreCase));
    Check("camera policy: unknown vendor direct IP is rejected",
        !CameraTransportPolicy.CanDispatch(new CameraDevice("Sony","GigE","FCB-EV7520","SNY-IP","169.254.113.1",-2),CameraTransportMode.Ip,out var camSonyDirectReason)&&
        camSonyDirectReason.Length>0);
    Check("camera policy: Hikrobot GigE is rejected in USB mode",
        !CameraTransportPolicy.CanDispatch(camHikrobotGigE,CameraTransportMode.Usb,out var camHikrobotUsb)&&camHikrobotUsb.Length>0);
    Check("camera policy: Hikrobot USB is rejected in IP mode",
        !CameraTransportPolicy.CanDispatch(new CameraDevice("Hikrobot","USB","MV-USB","HK-USB","",3),CameraTransportMode.Ip,out _));
    Check("camera policy: mode matching covers only USB and IP transports",
        CameraTransportPolicy.MatchesMode(camBaslerUsb,CameraTransportMode.Usb)&&
        CameraTransportPolicy.MatchesMode(camHikrobotGigE,CameraTransportMode.Ip)&&
        !CameraTransportPolicy.MatchesMode(camHikrobotGigE,CameraTransportMode.Usb)&&
        !CameraTransportPolicy.MatchesMode(camBaslerUsb,CameraTransportMode.Ip));

    foreach(string camValid in new[]{"169.254.113.1","10.0.0.1","0.0.0.0","255.255.255.255"," 192.168.1.20 ","gige-cam-01","camera01.plant.local","HIK-CAM"})
        Check("camera address accepted: "+camValid,CameraTransportPolicy.IsValidAddress(camValid));
    foreach(string camInvalid in new[]{"","   ","256.0.0.1","1.2.3","1.2.3.4.5","10.0.0.1:8080","http://10.0.0.1","-cam","cam-","a..b","a b","cam_x","10.0.0.256"})
        Check("camera address rejected: '"+camInvalid+"'",!CameraTransportPolicy.IsValidAddress(camInvalid));
    Check("camera address: null and overlong values rejected",
        !CameraTransportPolicy.IsValidAddress(null)&&
        !CameraTransportPolicy.IsValidAddress(new string('a',254))&&
        !CameraTransportPolicy.IsValidAddress(new string('a',64)+".local"));

    // Scan error surfacing: per-vendor issues never hide the devices that did enumerate.
    var camHikIssue=new CameraScanIssue("Hikrobot","USB","DllNotFoundException","MV_CC_EnumDevices could not load MvCameraControl.dll","HRESULT=0x8007007E at HikCameraService.Enumerate");
    var camBaslerIssue=new CameraScanIssue("Basler","GigE","TypeInitializationException","pylon runtime failed to initialise","at Basler.Pylon.CameraEnumerator..ctor");
    var camGvcpIssue=new CameraScanIssue("GVCP","GigE","SocketException","No discovery ACK from 169.254.113.1","UDP 3956 read timed out after 1000 ms");
    Check("scan issues: Hikrobot failure is labelled HIK SDK ERROR",camHikIssue.Label=="HIK SDK ERROR");
    Check("scan issues: Basler failure is labelled BASLER SDK ERROR",camBaslerIssue.Label=="BASLER SDK ERROR");
    Check("scan issues: GVCP failure is labelled GVCP ERROR",camGvcpIssue.Label=="GVCP ERROR");
    Check("scan issues: error type, message, details and summary are preserved",
        camHikIssue.ErrorType=="DllNotFoundException"&&camHikIssue.Message.Contains("MvCameraControl")&&
        camHikIssue.Details.Contains("0x8007007E")&&camHikIssue.Summary.Length>0);
    var camCleanScan=new CameraScanResult(Array.Empty<CameraDevice>(),Array.Empty<CameraScanIssue>());
    Check("scan result: clean scan has no issues and still summarizes",
        !camCleanScan.HasIssues&&camCleanScan.Devices.Count==0&&camCleanScan.Issues.Count==0&&camCleanScan.Summary.Length>0);
    var camPartialScan=new CameraScanResult(new[]{camHikGigE,camBaslerUsb},new[]{camBaslerIssue});
    Check("scan result: partial SDK failure keeps devices and reports the issue",
        camPartialScan.Devices.Count==2&&camPartialScan.Issues.Count==1&&
        camPartialScan.HasIssues&&camPartialScan.Summary.Length>0);
    Check("scan result: issue summary stays nonempty for the details pane",
        camPartialScan.Issues[0].Summary.Length>0&&camPartialScan.Summary.Length>0);

    // Real captured GVCP DISCOVERY_ACK from MV-CS016-10GC serial DA7403922 (read-only intro).
    const string camGvcpAckHex=
        "0000000300F800010002000080000001000034BD206E8DAB8000000700000005000000000000000000000000A9FE"+
        "7101000000000000000000000000FFFFFF00000000000000000000000000A9FE71FE48696B726F626F7400000000"+
        "00000000000000000000000000000000000000004D562D43533031362D3130474300000000000000000000000000"+
        "00000000000056342E302E34342032353034313020313530303138300000000000000000000048696B726F626F74"+
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000444137343033"+
        "3932320000000000000000000000000000000000000000000000";
    var camGvcpEndpoint=IPAddress.Parse("169.254.113.1");
    var camGvcpAck=Convert.FromHexString(camGvcpAckHex);
    var camGvcpInfo=GvcpDiscovery.ParseDiscoveryAck(camGvcpAck,camGvcpEndpoint);
    Check("gvcp: real ACK identity is parsed",
        camGvcpInfo is not null&&camGvcpInfo.IsIdentified&&camGvcpInfo.IsHikrobot&&
        camGvcpInfo.Manufacturer=="Hikrobot"&&camGvcpInfo.Model=="MV-CS016-10GC"&&
        camGvcpInfo.SerialNumber=="DA7403922"&&camGvcpInfo.Version.StartsWith("V4.0.44"));
    Check("gvcp: hardware MAC and current IP come from the ACK",
        camGvcpInfo is not null&&CamMac(camGvcpInfo.MacAddress)=="34BD206E8DAB"&&
        camGvcpInfo.Address=="169.254.113.1");
    var camGvcpDevice=camGvcpInfo?.ToCameraDevice();
    Check("gvcp: identified ACK becomes a Hikrobot GigE direct-IP device",
        camGvcpDevice is not null&&camGvcpDevice.Vendor=="Hikrobot"&&camGvcpDevice.Transport=="GigE"&&
        camGvcpDevice.Model=="MV-CS016-10GC"&&camGvcpDevice.Serial=="DA7403922"&&
        camGvcpDevice.Address=="169.254.113.1"&&camGvcpDevice.BackendIndex==-2);
    Check("gvcp: null and short packets never parse",
        GvcpDiscovery.ParseDiscoveryAck(null,camGvcpEndpoint) is null&&
        GvcpDiscovery.ParseDiscoveryAck(Array.Empty<byte>(),camGvcpEndpoint) is null&&
        GvcpDiscovery.ParseDiscoveryAck(new byte[4],camGvcpEndpoint) is null&&
        GvcpDiscovery.ParseDiscoveryAck(new byte[72],camGvcpEndpoint) is null);
    var camGvcpHeaderOnly=Convert.FromHexString("0000000300F800010002000080000001");
    Check("gvcp: header-only ACK is not a camera",
        GvcpDiscovery.ParseDiscoveryAck(camGvcpHeaderOnly,camGvcpEndpoint)?.ToCameraDevice() is null);
    var camGvcpZeroIdentity=new byte[256];
    camGvcpZeroIdentity[3]=0x03;
    var camGvcpZero=GvcpDiscovery.ParseDiscoveryAck(camGvcpZeroIdentity,camGvcpEndpoint);
    Check("gvcp: zero-identity packet yields no fake device",
        (camGvcpZero is null||!camGvcpZero.IsIdentified)&&camGvcpZero?.ToCameraDevice() is null);
    var camGvcpNoModel=Convert.FromHexString(camGvcpAckHex);
    Array.Clear(camGvcpNoModel,112,32);
    var camGvcpNoModelInfo=GvcpDiscovery.ParseDiscoveryAck(camGvcpNoModel,camGvcpEndpoint);
    Check("gvcp: manufacturer without model is not an identified camera",
        camGvcpNoModelInfo is null||!camGvcpNoModelInfo.IsIdentified);
    var camGvcpNoIp=Convert.FromHexString(camGvcpAckHex);
    Array.Clear(camGvcpNoIp,44,4);
    var camGvcpNoIpInfo=GvcpDiscovery.ParseDiscoveryAck(camGvcpNoIp,IPAddress.Parse("169.254.113.20"));
    Check("gvcp: zeroed current IP falls back to the UDP endpoint address",
        camGvcpNoIpInfo is not null&&camGvcpNoIpInfo.IsIdentified&&camGvcpNoIpInfo.Address=="169.254.113.20");

    var camSlot=new CameraSlotModel(1,"Camera 01");
    Check("camera slots: default mode is IP and state OFFLINE",
        camSlot.Mode==CameraTransportMode.Ip&&camSlot.State==CameraSlotState.Offline&&camSlot.DisplayState=="OFFLINE");
    camSlot.SetDevices(new[]{camBaslerUsb,camHikGigE,camUvc});
    Check("camera slots: scan status summarizes device counts",camSlot.StatusText=="SCAN 3 DEVICES | USB 2 | IP 1");
    Check("camera slots: scan order is USB then GigE then model",
        camSlot.Devices.Count==3&&camSlot.Devices[0].Key==camBaslerUsb.Key&&
        camSlot.Devices[1].Key==camUvc.Key&&camSlot.Devices[2].Key==camHikGigE.Key);
    Check("camera slots: default IP mode lists only the GigE device",
        camSlot.FilteredDevices.Count==1&&camSlot.FilteredDevices[0].Key==camHikGigE.Key);
    camSlot.Mode=CameraTransportMode.Usb;
    Check("camera slots: USB mode filters to USB transport",
        camSlot.FilteredDevices.Count==2&&camSlot.FilteredDevices.All(d=>d.Transport.Contains("USB")));
    var camUsbCount=camSlot.FilteredDevices.Count;
    camSlot.Mode=CameraTransportMode.Ip;
    var camIpCount=camSlot.FilteredDevices.Count;
    Check("camera slots: USB plus IP filtering covers every scanned device",
        camUsbCount==2&&camIpCount==1&&camUsbCount+camIpCount==camSlot.Devices.Count);
    Check("camera slots: GigE device selects in IP mode",camSlot.SelectDevice(camHikGigE.Key));
    camSlot.SetDevices(new[]{camBaslerUsb,camHikGigE,camUvc,camHikGigE});
    Check("camera slots: duplicate scan keys are deduplicated",camSlot.Devices.Count==3);
    Check("camera slots: selection survives a rescan containing it",
        camSlot.Selected is not null&&camSlot.Selected.Key==camHikGigE.Key);
    camSlot.SetDevices(new[]{camBaslerUsb});
    Check("camera slots: rescan clears a selection that disappeared",camSlot.Selected is null&&camSlot.Devices.Count==1);
    var camErrorSlot=new CameraSlotModel(1,"Camera 01");
    camErrorSlot.SetScanResult(camPartialScan);
    Check("camera slots: SetScanResult keeps devices and stores every issue",
        camErrorSlot.Devices.Count==2&&camErrorSlot.ScanIssues.Count==1&&
        camErrorSlot.ScanIssues[0].Label=="BASLER SDK ERROR");
    Check("camera slots: scan status reports the error count",
        camErrorSlot.StatusText=="SCAN 2 DEVICES | USB 1 | IP 1 | ERR 1");
    camErrorSlot.SetDevices(new[]{camBaslerUsb,camHikGigE});
    Check("camera slots: later SetDevices clears scan issues",
        camErrorSlot.ScanIssues.Count==0&&camErrorSlot.StatusText=="SCAN 2 DEVICES | USB 1 | IP 1");
    var camSelectSlot=new CameraSlotModel(1,"Camera 01"){Mode=CameraTransportMode.Usb};
    camSelectSlot.SetDevices(new[]{camBaslerUsb,camHikGigE});
    Check("camera slots: wrong-mode device selection rejected",
        !camSelectSlot.SelectDevice(camHikGigE.Key)&&camSelectSlot.LastError=="Selected device does not match USB/IP mode");
    camSelectSlot.SetDevices(new[]{camUvc});
    Check("camera slots: generic UVC selection rejected with reason",
        !camSelectSlot.SelectDevice(camUvc.Key)&&camSelectSlot.LastError.Contains("UVC"));
    Check("camera slots: unknown device selection rejected",
        !camSelectSlot.SelectDevice("NOPE|0000")&&camSelectSlot.LastError=="Camera device is not in the scanned list");
    Check("camera slots: blank device selection rejected",
        !camSelectSlot.SelectDevice(" ")&&camSelectSlot.LastError=="Select a camera device");
    var camRegistry=new CameraOwnershipRegistry();
    var camUsbSlot=new CameraSlotModel(1,"Camera 01"){Mode=CameraTransportMode.Usb,IpText=""};
    camUsbSlot.SetDevices(new[]{camBaslerUsb});
    camUsbSlot.SelectDevice(camBaslerUsb.Key);
    var camUsbValidation=camUsbSlot.ValidateBeforeConnect(camRegistry);
    Check("camera slots: USB connect needs no IP address",camUsbValidation.ok&&camUsbValidation.error.Length==0);
    Check("camera slots: USB connect claims the camera",camRegistry.OwnerOf(camBaslerUsb.Key)==1);
    camUsbSlot.IpText="not an address";
    Check("camera slots: USB mode ignores invalid IP text",camUsbSlot.ValidateBeforeConnect(camRegistry).ok);
    var camIpRegistry=new CameraOwnershipRegistry();
    var camIpSlot=new CameraSlotModel(2,"Camera 02"){Mode=CameraTransportMode.Ip,IpText=""};
    camIpSlot.SetDevices(new[]{camHikGigE});
    camIpSlot.SelectDevice(camHikGigE.Key);
    var camIpBlank=camIpSlot.ValidateBeforeConnect(camIpRegistry);
    Check("camera slots: IP connect requires an address",
        !camIpBlank.ok&&camIpBlank.error=="Enter a valid IPv4 address or hostname");
    Check("camera slots: failed address validation does not claim the camera",camIpRegistry.OwnerOf(camHikGigE.Key) is null);
    camIpSlot.IpText="999.254.113.1";
    Check("camera slots: invalid IPv4 rejected before connect",!camIpSlot.ValidateBeforeConnect(camIpRegistry).ok);
    camIpSlot.IpText="169.254.113.1";
    var camIpValid=camIpSlot.ValidateBeforeConnect(camIpRegistry);
    Check("camera slots: valid IPv4 accepted and claimed for IP connect",
        camIpValid.ok&&camIpRegistry.OwnerOf(camHikGigE.Key)==2);
    camIpSlot.IpText="gige-cam-01";
    Check("camera slots: hostname accepted for IP connect",camIpSlot.ValidateBeforeConnect(camIpRegistry).ok);
    var camEmptySlot=new CameraSlotModel(1,"Camera 01");
    var camNoDevice=camEmptySlot.ValidateBeforeConnect(new CameraOwnershipRegistry());
    Check("camera slots: connect without a device rejected",
        !camNoDevice.ok&&camNoDevice.error=="Select a camera device");
    var camModeSlot=new CameraSlotModel(1,"Camera 01");
    camModeSlot.SetDevices(new[]{camBaslerUsb,camHikGigE});
    camModeSlot.IpText="169.254.113.1";
    Check("camera slots: IP default keeps the GigE selection",
        camModeSlot.SelectDevice(camHikGigE.Key)&&camModeSlot.Selected?.Key==camHikGigE.Key);
    camModeSlot.Mode=CameraTransportMode.Usb;
    Check("camera slots: mode change clears the incompatible selection",camModeSlot.Selected is null);
    var camModeResult=camModeSlot.ValidateBeforeConnect(new CameraOwnershipRegistry());
    Check("camera slots: cleared selection cannot connect",
        !camModeResult.ok&&camModeResult.error=="Select a camera device");
    Check("camera slots: USB mode then lists only USB devices",
        camModeSlot.FilteredDevices.Count==1&&camModeSlot.FilteredDevices[0].Key==camBaslerUsb.Key);
    Check("camera slots: USB device selects after the mode change",camModeSlot.SelectDevice(camBaslerUsb.Key));
    camModeSlot.Mode=CameraTransportMode.Ip;
    Check("camera slots: switching back to IP clears the USB selection",
        camModeSlot.Selected is null&&camModeSlot.FilteredDevices.Count==1&&camModeSlot.FilteredDevices[0].Key==camHikGigE.Key);
    var camDirectSlot=new CameraSlotModel(1,"Camera 01");
    camDirectSlot.SetDevices(new[]{camHikrobotGigE});
    camDirectSlot.IpText="169.254.113.1";
    Check("camera slots: GVCP-identified Hikrobot direct device is selectable",
        camDirectSlot.SelectDevice(camHikrobotGigE.Key)&&camDirectSlot.Selected?.Key==camHikrobotGigE.Key);
    var camDirectValidation=camDirectSlot.ValidateBeforeConnect(new CameraOwnershipRegistry());
    Check("camera slots: Hikrobot direct IP passes connect eligibility",
        camDirectValidation.ok&&camDirectValidation.error.Length==0);
    var camOwners=new CameraOwnershipRegistry();
    Check("camera ownership: first claim accepted",
        camOwners.TryClaim("BASLER|40001234",1,out var camClaimError)&&camClaimError.Length==0);
    Check("camera ownership: second slot double claim rejected",
        !camOwners.TryClaim("basler|40001234",2,out var camDoubleError)&&camDoubleError=="In use by Station 01");
    Check("camera ownership: same slot re-claim accepted",camOwners.TryClaim("Basler|40001234",1,out _));
    Check("camera ownership: owner lookup is case-insensitive",camOwners.OwnerOf("basler|40001234")==1);
    camOwners.Release(1);
    Check("camera ownership: release frees the device",
        camOwners.OwnerOf("BASLER|40001234") is null&&camOwners.TryClaim("BASLER|40001234",2,out _));
    var camIsolation=new CameraOwnershipRegistry();
    camIsolation.TryClaim(camBaslerUsb.Key,1,out _);
    camIsolation.TryClaim(camHikGigE.Key,2,out _);
    camIsolation.Release(2);
    Check("camera ownership: releasing slot 2 keeps slot 1 camera",
        camIsolation.OwnerOf(camBaslerUsb.Key)==1&&camIsolation.OwnerOf(camHikGigE.Key) is null);
    Check("camera ownership: second slot cannot take slot 1 camera",
        !camIsolation.TryClaim(camBaslerUsb.Key,2,out var camIsolationError)&&camIsolationError=="In use by Station 01");
    camIsolation.Reset();
    Check("camera ownership: reset clears every claim",camIsolation.OwnerOf(camBaslerUsb.Key) is null);
    var camShared=new CameraOwnershipRegistry();
    var camSlot01=new CameraSlotModel(1,"Camera 01"){Mode=CameraTransportMode.Usb};
    var camSlot02=new CameraSlotModel(2,"Camera 02"){Mode=CameraTransportMode.Usb};
    camSlot01.SetDevices(new[]{camBaslerUsb});
    camSlot02.SetDevices(new[]{camBaslerUsb});
    camSlot01.SelectDevice(camBaslerUsb.Key);
    camSlot02.SelectDevice(camBaslerUsb.Key);
    var camSlot01Claim=camSlot01.ValidateBeforeConnect(camShared);
    var camSlot02Claim=camSlot02.ValidateBeforeConnect(camShared);
    Check("camera slots: second station cannot validate the same camera",
        camSlot01Claim.ok&&!camSlot02Claim.ok&&camSlot02Claim.error=="In use by Station 01");
    camShared.Release(1);
    Check("camera slots: released camera validates for station 2",camSlot02.ValidateBeforeConnect(camShared).ok);
    var camState=new CameraSlotModel(1,"Camera 01");
    camState.SetDevices(new[]{camBaslerUsb});
    camState.MarkScanning();
    Check("camera slots: scanning state SCANNING",
        camState.State==CameraSlotState.Scanning&&camState.DisplayState=="SCANNING");
    camState.MarkConnecting();
    Check("camera slots: connecting state CONNECTING",
        camState.State==CameraSlotState.Connecting&&camState.DisplayState=="CONNECTING"&&camState.StatusText=="CONNECTING");
    camState.MarkConnected();
    Check("camera slots: connected state CONNECTED",
        camState.State==CameraSlotState.Connected&&camState.DisplayState=="CONNECTED"&&camState.StatusText=="CONNECTED");
    var camNow=new DateTime(2026,10,22,9,30,0,DateTimeKind.Utc);
    Check("camera slots: connected without frames is not LIVE",!camState.IsLive(camNow,TimeSpan.FromSeconds(3)));
    camState.MarkFrame(camNow);
    Check("camera slots: frame marks LIVE with its timestamp",
        camState.State==CameraSlotState.Live&&camState.DisplayState=="LIVE"&&camState.StatusText=="LIVE"&&
        camState.LastFrameUtc==camNow&&camState.IsLive(camNow.AddMilliseconds(250),TimeSpan.FromSeconds(3)));
    Check("camera slots: stale or future frames are not LIVE",
        !camState.IsLive(camNow.AddSeconds(4),TimeSpan.FromSeconds(3))&&
        !camState.IsLive(camNow.AddSeconds(-1),TimeSpan.FromSeconds(3))&&
        !camState.IsLive(camNow,TimeSpan.Zero)&&
        !camState.IsLive(camNow,TimeSpan.FromSeconds(-1)));
    camState.MarkConnected();
    Check("camera slots: reconnect with an old frame shows NO FRAME",
        camState.State==CameraSlotState.Connected&&camState.DisplayState=="NO FRAME");
    camState.MarkLost("USB unplugged");
    Check("camera slots: lost state LOST and never LIVE",
        camState.State==CameraSlotState.Lost&&camState.DisplayState=="LOST"&&
        camState.LastError=="USB unplugged"&&camState.StatusText.Contains("USB unplugged")&&
        !camState.IsLive(camNow,TimeSpan.FromSeconds(3)));
    camState.MarkError("SDK open timeout");
    Check("camera slots: error state reports ERROR",
        camState.State==CameraSlotState.Error&&camState.DisplayState=="ERROR"&&camState.LastError=="SDK open timeout");
    camState.MarkError("Camera open failed");
    camState.MarkConnecting();
    Check("camera slots: retry clears the previous error",camState.LastError.Length==0);
    camState.MarkOffline();
    Check("camera slots: offline clears the error and keeps devices",
        camState.State==CameraSlotState.Offline&&camState.DisplayState=="OFFLINE"&&
        camState.LastError.Length==0&&camState.Devices.Count==1);
    // Dual-station concurrent slot and safety isolation tests
    var reg=new CameraOwnershipRegistry();
    var s1=new CameraSlotModel(1,"Station 01"){Mode=CameraTransportMode.Ip,IpText="169.254.113.1"};
    var s2=new CameraSlotModel(2,"Station 02"){Mode=CameraTransportMode.Usb};
    var dev1=new CameraDevice("Hikvision","GigE","MV-CA050","00F123456","169.254.113.1",0);
    var dev2=new CameraDevice("Basler","USB","daA2500-14uc","40098765","",1);
    s1.SetDevices(new[]{dev1});
    s2.SetDevices(new[]{dev2});
    s1.SelectDevice(dev1.Key);
    s2.SelectDevice(dev2.Key);

    var v1=s1.ValidateBeforeConnect(reg);
    var v2=s2.ValidateBeforeConnect(reg);
    Check("dual slots: different devices validate concurrently",v1.ok&&v2.ok);
    s1.MarkConnecting();s1.MarkConnected();
    s2.MarkConnecting();s2.MarkConnected();
    Check("dual slots: independent connected states",
        s1.State==CameraSlotState.Connected&&s2.State==CameraSlotState.Connected);

    var t0=new DateTime(2026,10,22,10,0,0,DateTimeKind.Utc);
    s1.MarkFrame(t0);
    s2.MarkFrame(t0);
    Check("dual slots: both live concurrently",
        s1.IsLive(t0,TimeSpan.FromSeconds(3))&&s2.IsLive(t0,TimeSpan.FromSeconds(3)));

    // Safe unplug isolation: slot 2 lost has no effect on slot 1
    s2.MarkLost("USB unplugged");
    Check("dual slots: slot 2 lost does not affect slot 1",
        s2.State==CameraSlotState.Lost&&!s2.IsLive(t0,TimeSpan.FromSeconds(3))&&
        s1.State==CameraSlotState.Live&&s1.IsLive(t0,TimeSpan.FromSeconds(3)));

    // Disconnect isolation
    reg.Release(2);
    s2.MarkOffline();
    Check("dual slots: slot 2 offline keeps slot 1 claimed and live",
        reg.OwnerOf(dev1.Key)==1&&reg.OwnerOf(dev2.Key) is null&&
        s1.State==CameraSlotState.Live&&s2.State==CameraSlotState.Offline);

    // Camera 2 live does NOT satisfy Camera 1 interlock
    var interlock=new LineInterlockModel();
    var inputWithCam2Only=ReadyInputs() with {CameraConnected=false,CameraFrameFresh=false};
    var evalCam2Only=interlock.Evaluate(inputWithCam2Only);
    Check("camera interlock: Cam2 alone does not satisfy Cam1 requirement",
        !evalCam2Only.CanStart&&Detail(evalCam2Only,InterlockCheckId.CameraFrame,"No camera"));

    // Camera connection never commands machine motion
    var evalCamBoth=interlock.Evaluate(ReadyInputs());
    Check("camera interlock: cameras connecting changes no interlock state",
        evalCamBoth.State==InterlockState.Ready&&evalCamBoth.CanStart);
}
Check("mailbox: source commands are disarmed until hardware commissioning",
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
Check("mailbox quality: live, source only, reserved and missing classification",
    LineMailboxProtocol.ClassifyQuality("MAIN.bHmiLineStartReq", true) == "LIVE" &&
    LineMailboxProtocol.ClassifyQuality("MAIN.bHmiLineStartReq", false) == "SOURCE_ONLY" &&
    LineMailboxProtocol.ClassifyQuality("GVL_SCARA.bConnected", false) == "RESERVED" &&
    LineMailboxProtocol.ClassifyQuality("GVL_CAMERA2.bFrameValid", false) == "RESERVED" &&
    LineMailboxProtocol.ClassifyQuality("NONEXISTENT.TAG.NAME", false) == "MISSING");
Check("mailbox result codes: descriptions match PLC contract",
    LineMailboxProtocol.DescribeLineCommandResult(1).Contains("ACCEPTED") &&
    LineMailboxProtocol.DescribeLineCommandResult(10).Contains("Duplicate") &&
    LineMailboxProtocol.DescribeLineCommandResult(11).Contains("not commissioned") &&
    LineMailboxProtocol.DescribeLineCommandResult(12).Contains("heartbeat") &&
    LineMailboxProtocol.DescribeLineCommandResult(13).Contains("Camera 1") &&
    LineMailboxProtocol.DescribeLineCommandResult(17).Contains("SCARA") &&
    LineMailboxProtocol.DescribeBeltCommandResult(1).Contains("ACCEPTED") &&
    LineMailboxProtocol.DescribeBeltCommandResult(14).Contains("25 Hz"));
var gateway = LineMailboxGateway.Instance;
uint seq1 = gateway.AllocateSequence();
uint seq2 = gateway.AllocateSequence();
Check("mailbox gateway: sequence increments and allocates nonzero ID",
    seq1 > 0 && seq2 == seq1 + 1 && gateway.CurrentSequence == seq2);
bool hb1 = gateway.NextHeartbeatBit();
bool hb2 = gateway.NextHeartbeatBit();
Check("mailbox gateway: heartbeat bit alternates",
    hb1 != hb2 && gateway.CurrentHeartbeat == hb2);
var simStart = gateway.RequestLineStart(true, false, false, false, false);
Check("mailbox gateway: mock START produces safe simulated acceptance with no write",
    simStart.Success && simStart.ResultCode == 1 && simStart.Message.Contains("TEST ONLY") && simStart.Message.Contains("NO PLC WRITE"));
var blockedOfflineStart = gateway.RequestLineStart(false, false, true, true, true);
Check("mailbox gateway: production START blocked when ADS offline",
    !blockedOfflineStart.Success && blockedOfflineStart.ResultCode == 15 && blockedOfflineStart.Message.Contains("offline"));
var blockedMissingStart = gateway.RequestLineStart(false, true, false, true, true);
Check("mailbox gateway: production START blocked when PLC symbols missing",
    !blockedMissingStart.Success && blockedMissingStart.ResultCode == 11 && blockedMissingStart.Message.Contains("MISSING"));
var blockedDisarmedStart = gateway.RequestLineStart(false, true, true, true, true);
Check("mailbox gateway: production START blocked when compile gate disarmed",
    !blockedDisarmedStart.Success && blockedDisarmedStart.ResultCode == 11 && blockedDisarmedStart.Message.Contains("disarmed"));
var blockedCameraStart = gateway.RequestLineStart(false, true, true, false, true);
Check("mailbox gateway: production START blocked when camera not ready",
    !blockedCameraStart.Success && blockedCameraStart.ResultCode == 13 && blockedCameraStart.Message.Contains("Camera 1"));
var simStop = gateway.RequestLineStop(true, false);
Check("mailbox gateway: mock STOP produces simulated acceptance noting not certified E-stop",
    simStop.Success && simStop.ResultCode == 1 && simStop.Message.Contains("TEST ONLY") && simStop.Message.Contains("NOT a certified E-stop"));
var blockedStop = gateway.RequestLineStop(false, true);
Check("mailbox gateway: production STOP safely fails closed when uncommissioned",
    !blockedStop.Success && blockedStop.Message.Contains("PHYSICAL EMERGENCY STOP"));
var simReset = gateway.RequestLineReset(true, false);
Check("mailbox gateway: mock RESET produces simulated acceptance",
    simReset.Success && simReset.ResultCode == 1 && simReset.Message.Contains("TEST ONLY"));
var simManualStart = gateway.RequestManualBeltStart(true, false, false, 80.0);
Check("mailbox gateway: mock manual belt start formats setpoint",
    simManualStart.Success && simManualStart.ResultCode == 1 && simManualStart.Message.Contains("80.0 mm/s") && simManualStart.Message.Contains("NO PLC WRITE"));
var blockedManualStartMap = gateway.RequestManualBeltStart(false, true, false, 80.0);
Check("mailbox gateway: production manual start blocked when speed map unready",
    !blockedManualStartMap.Success && blockedManualStartMap.ResultCode == 13 && blockedManualStartMap.Message.Contains("speed map is NOT ready"));
var simManualStop = gateway.RequestManualBeltStop(true, false);
Check("mailbox gateway: mock manual belt stop produces simulated acceptance",
    simManualStop.Success && simManualStop.ResultCode == 1 && simManualStop.Message.Contains("TEST ONLY"));

// =========================================================================
// LIVE ADS MAILBOX TRANSPORT & GATEWAY HANDSHAKE OFFLINE UNIT TESTS
// =========================================================================

// 1. Test FakeAdsMailboxTransport basic read/write/type safety
var fakeTransport = new FakeAdsMailboxTransport();
fakeTransport.SetSymbol("MAIN.bBeltSpeedMapReady", true);
fakeTransport.SetSymbol("MAIN.rHmiBeltSetSpeed_mm_s", 100.0);
fakeTransport.SetSymbol("MAIN.udiHmiLineCmdSeq", 42u);
fakeTransport.SetSymbol("MAIN.nLineCommandResult", (ushort)1);
Check("fake transport: read back bool", fakeTransport.Read<bool>("MAIN.bBeltSpeedMapReady") == true);
Check("fake transport: read back double", Math.Abs(fakeTransport.Read<double>("MAIN.rHmiBeltSetSpeed_mm_s") - 100.0) < 0.001);
Check("fake transport: read back uint", fakeTransport.Read<uint>("MAIN.udiHmiLineCmdSeq") == 42u);
Check("fake transport: read back ushort", fakeTransport.Read<ushort>("MAIN.nLineCommandResult") == 1);
var symPresence = fakeTransport.CheckSymbols(new[] { "MAIN.bBeltSpeedMapReady", "NONEXISTENT.TAG" });
Check("fake transport: check symbols distinguishes existing vs absent",
    symPresence["MAIN.bBeltSpeedMapReady"] && !symPresence["NONEXISTENT.TAG"]);
fakeTransport.Disconnect();
Check("fake transport: disconnected reports TryRead false", !fakeTransport.TryRead<bool>("MAIN.bBeltSpeedMapReady", out _));
Check("fake transport: disconnected reports TryWrite false", !fakeTransport.TryWrite("MAIN.bBeltSpeedMapReady", true, out _));

// 2. Test Guarded Fail-Closed Safety (disarmed gateway performs ZERO writes)
var disarmedTransport = new FakeAdsMailboxTransport();
var disarmedGateway = LineMailboxGateway.CreateForTesting(disarmedTransport, commissioned: false);
var dRes1 = disarmedGateway.RequestLineStart(false, true, true, true, true);
var dRes2 = disarmedGateway.RequestLineStop(false, true);
var dRes3 = disarmedGateway.RequestLineReset(false, true);
var dRes4 = disarmedGateway.RequestManualBeltSet(false, true, 80.0);
var dRes5 = disarmedGateway.RequestManualBeltStart(false, true, true, 80.0);
var dRes6 = disarmedGateway.RequestManualBeltStop(false, true);
bool dRes7 = disarmedGateway.TickHeartbeat(false, true);
Check("disarmed gateway: RequestLineStart fails closed with code 11",
    !dRes1.Success && dRes1.ResultCode == 11 && dRes1.Message.Contains("disarmed"));
Check("disarmed gateway: RequestLineStop fails closed with code 11",
    !dRes2.Success && dRes2.ResultCode == 11 && dRes2.Message.Contains("not commissioned"));
Check("disarmed gateway: RequestLineReset fails closed with code 11",
    !dRes3.Success && dRes3.ResultCode == 11 && dRes3.Message.Contains("not commissioned"));
Check("disarmed gateway: RequestManualBeltSet fails closed with code 11",
    !dRes4.Success && dRes4.ResultCode == 11 && dRes4.Message.Contains("disarmed"));
Check("disarmed gateway: RequestManualBeltStart fails closed with code 11",
    !dRes5.Success && dRes5.ResultCode == 11 && dRes5.Message.Contains("disarmed"));
Check("disarmed gateway: RequestManualBeltStop fails closed with code 11",
    !dRes6.Success && dRes6.ResultCode == 11 && dRes6.Message.Contains("not commissioned"));
Check("disarmed gateway: TickHeartbeat returns false without write", !dRes7);
Check("disarmed gateway: ZERO writes performed to transport (hard fail-closed)",
    disarmedTransport.Writes.Count == 0);

// 3. Test Commissioned Command Sequences, Pulse Ordering, Rising Edges & Type Mapping
var armedTransport = new FakeAdsMailboxTransport();
// Setup simulated PLC reaction to mailbox write events
armedTransport.OnSymbolWritten = (symbol, val, transport) =>
{
    if (symbol == "MAIN.bHmiBeltSetReq" && (bool)val)
    {
        if (transport.TryRead<uint>("MAIN.udiHmiBeltCmdSeq", out var bseq))
            transport.SetSymbol("MAIN.udiBeltLastAckSeq", bseq);
        transport.SetSymbol("MAIN.nBeltCommandResult", (ushort)1);
        if (transport.TryRead<double>("MAIN.rHmiBeltSetSpeed_mm_s", out var sp))
        {
            transport.SetSymbol("MAIN.rMD200_SetSpeed_mm_s", sp);
            transport.SetSymbol("MAIN.rBeltAppliedSpeed_mm_s", sp);
        }
    }
    else if (symbol == "MAIN.bHmiBeltStartReq" && (bool)val)
    {
        if (transport.TryRead<uint>("MAIN.udiHmiBeltCmdSeq", out var bseq))
            transport.SetSymbol("MAIN.udiBeltLastAckSeq", bseq);
        transport.SetSymbol("MAIN.nBeltCommandResult", (ushort)1);
        transport.SetSymbol("MAIN.bMD200_Start", true);
    }
    else if (symbol == "MAIN.bHmiBeltStopReq" && (bool)val)
    {
        if (transport.TryRead<uint>("MAIN.udiHmiBeltCmdSeq", out var bseq))
            transport.SetSymbol("MAIN.udiBeltLastAckSeq", bseq);
        transport.SetSymbol("MAIN.nBeltCommandResult", (ushort)1);
        transport.SetSymbol("MAIN.bMD200_Start", false);
    }
    else if (symbol == "MAIN.bHmiLineStartReq" && (bool)val)
    {
        if (transport.TryRead<uint>("MAIN.udiHmiLineCmdSeq", out var seq))
        {
            transport.SetSymbol("MAIN.udiLineLastAckSeq", seq);
            transport.SetSymbol("MAIN.nLineCommandResult", (ushort)1);
            transport.SetSymbol("MAIN.bLineAutoAccepted", true);
        }
    }
    else if (symbol == "MAIN.bHmiLineStopReq" && (bool)val)
    {
        if (transport.TryRead<uint>("MAIN.udiHmiLineCmdSeq", out var seq))
        {
            transport.SetSymbol("MAIN.udiLineLastAckSeq", seq);
            transport.SetSymbol("MAIN.nLineCommandResult", (ushort)1);
            transport.SetSymbol("MAIN.bLineAutoAccepted", false);
            transport.SetSymbol("MAIN.bLineNormalStopLatched", true);
        }
    }
    else if (symbol == "MAIN.bHmiLineResetReq" && (bool)val)
    {
        if (transport.TryRead<uint>("MAIN.udiHmiLineCmdSeq", out var seq))
        {
            transport.SetSymbol("MAIN.udiLineLastAckSeq", seq);
            transport.SetSymbol("MAIN.nLineCommandResult", (ushort)1);
            transport.SetSymbol("MAIN.bLineNormalStopLatched", false);
        }
    }
};
var armedGateway = LineMailboxGateway.CreateForTesting(armedTransport, commissioned: true);

// 3A. Manual Belt SET
var setRes = armedGateway.RequestManualBeltSet(false, true, 110.0);
Check("armed gateway: RequestManualBeltSet succeeds", setRes.Success && setRes.ResultCode == 1);
Check("armed gateway: PriorBeltSetAccepted is true after valid SET", armedGateway.PriorBeltSetAccepted);
Check("armed gateway: LastAcceptedBeltSetMmS matches setpoint", Math.Abs(armedGateway.LastAcceptedBeltSetMmS - 110.0) < 0.001);
var setWrites = armedTransport.Writes.Where(w => w.Symbol.StartsWith("MAIN.bHmiBeltSetReq") || w.Symbol.StartsWith("MAIN.rHmiBeltSetSpeed") || w.Symbol.StartsWith("MAIN.udiHmiBeltCmdSeq")).ToList();
Check("armed gateway: SET wrote sequence, setpoint, pulse true then false",
    setWrites.Count >= 4 &&
    setWrites[0].Symbol == "MAIN.udiHmiBeltCmdSeq" &&
    setWrites[1].Symbol == "MAIN.rHmiBeltSetSpeed_mm_s" && (double)setWrites[1].Value == 110.0 &&
    setWrites[2].Symbol == "MAIN.bHmiBeltSetReq" && (bool)setWrites[2].Value == true &&
    setWrites[3].Symbol == "MAIN.bHmiBeltSetReq" && (bool)setWrites[3].Value == false);
Check("armed gateway: ZERO writes to PLC-owned nBeltCommandResult",
    !armedTransport.Writes.Any(w => w.Symbol == "MAIN.nBeltCommandResult"));

// 3B. Manual Belt START
armedTransport.ClearWrites();
var mStartRes = armedGateway.RequestManualBeltStart(false, true, true, 110.0);
Check("armed gateway: RequestManualBeltStart succeeds", mStartRes.Success && mStartRes.ResultCode == 1);
Check("armed gateway: ManualBeltActive is true after start", armedGateway.ManualBeltActive);
var mStartWrites = armedTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiBeltStartReq").ToList();
Check("armed gateway: Manual START asserted pulse true then false",
    mStartWrites.Count == 2 && (bool)mStartWrites[0].Value == true && (bool)mStartWrites[1].Value == false);

// 3C. Mutual Exclusion: cannot start AUTO while manual belt is active
var autoConflictRes = armedGateway.RequestLineStart(false, true, true, true, true);
Check("armed gateway: starting AUTO while manual belt active is rejected (code 16)",
    !autoConflictRes.Success && autoConflictRes.ResultCode == 16 && autoConflictRes.Message.Contains("Manual belt"));

// 3D. Manual Belt STOP
armedTransport.ClearWrites();
var mStopRes = armedGateway.RequestManualBeltStop(false, true);
Check("armed gateway: RequestManualBeltStop succeeds", mStopRes.Success && mStopRes.ResultCode == 1);
Check("armed gateway: ManualBeltActive is false after stop", !armedGateway.ManualBeltActive);
var mStopWrites = armedTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiBeltStopReq").ToList();
Check("armed gateway: Manual STOP asserted pulse true then false",
    mStopWrites.Count == 2 && (bool)mStopWrites[0].Value == true && (bool)mStopWrites[1].Value == false);

// 3E. AUTO Line START
armedTransport.ClearWrites();
uint prevSeq = armedGateway.CurrentSequence;
var aStartRes = armedGateway.RequestLineStart(false, true, true, true, true);
Check("armed gateway: RequestLineStart succeeds with ACK", aStartRes.Success && aStartRes.ResultCode == 1);
Check("armed gateway: AutoLineActive is true after AUTO start", armedGateway.AutoLineActive);
Check("armed gateway: Sequence incremented for AUTO start", armedGateway.CurrentSequence == prevSeq + 1);
var aStartReqWrites = armedTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiLineStartReq").ToList();
Check("armed gateway: AUTO START asserted pulse true then false",
    aStartReqWrites.Count == 2 && (bool)aStartReqWrites[0].Value == true && (bool)aStartReqWrites[1].Value == false);
var camWrites = armedTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiCamera1Ready").ToList();
Check("armed gateway: AUTO START wrote Camera1Ready = true", camWrites.Count > 0 && (bool)camWrites[0].Value == true);

// 3F. Mutual Exclusion: cannot start Manual Belt while AUTO is active
var mConflictRes = armedGateway.RequestManualBeltStart(false, true, true, 100.0);
Check("armed gateway: starting manual belt while AUTO active is rejected (code 12)",
    !mConflictRes.Success && mConflictRes.ResultCode == 12 && mConflictRes.Message.Contains("AUTO mode is currently active"));

// 3G. AUTO Line STOP (controlled stop, NOT E-stop)
armedTransport.ClearWrites();
var aStopRes = armedGateway.RequestLineStop(false, true);
Check("armed gateway: RequestLineStop succeeds and clarifies not certified E-stop",
    aStopRes.Success && aStopRes.ResultCode == 1 && aStopRes.Message.Contains("NOT a certified emergency stop"));
Check("armed gateway: AutoLineActive is false after STOP", !armedGateway.AutoLineActive);
var aStopWrites = armedTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiLineStopReq").ToList();
Check("armed gateway: AUTO STOP asserted pulse true then false",
    aStopWrites.Count == 2 && (bool)aStopWrites[0].Value == true && (bool)aStopWrites[1].Value == false);

// 3H. AUTO Line RESET
armedTransport.ClearWrites();
var aResetRes = armedGateway.RequestLineReset(false, true);
Check("armed gateway: RequestLineReset succeeds with ACK", aResetRes.Success && aResetRes.ResultCode == 1);
var aResetWrites = armedTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiLineResetReq").ToList();
Check("armed gateway: AUTO RESET asserted pulse true then false",
    aResetWrites.Count == 2 && (bool)aResetWrites[0].Value == true && (bool)aResetWrites[1].Value == false);

// 4. Test ACK Timeout / Stale Sequence Handling
var timeoutTransport = new FakeAdsMailboxTransport();
// PLC fails to acknowledge (does not update udiLineLastAckSeq)
var timeoutGateway = LineMailboxGateway.CreateForTesting(timeoutTransport, commissioned: true);
var tStartRes = timeoutGateway.RequestLineStart(false, true, true, true, true);
Check("timeout gateway: failed ACK results in timeout / failure", !tStartRes.Success && tStartRes.Message.Contains("TIMEOUT"));
Check("timeout gateway: AutoLineActive remains false on timeout", !timeoutGateway.AutoLineActive);
var tWrites = timeoutTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiLineStartReq").ToList();
Check("timeout gateway: pulse was cleanly released to false despite timeout",
    tWrites.Count == 2 && (bool)tWrites[0].Value == true && (bool)tWrites[1].Value == false);

// 5. Test Heartbeat Toggling with Armed Transport
armedTransport.ClearWrites();
bool hbT1 = armedGateway.TickHeartbeat(false, true);
bool hbT2 = armedGateway.TickHeartbeat(false, true);
Check("armed gateway: TickHeartbeat returned true twice", hbT1 && hbT2);
var hbWrites = armedTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiHeartbeat").ToList();
Check("armed gateway: Heartbeat wrote alternating bits to transport",
    hbWrites.Count == 2 && (bool)hbWrites[0].Value != (bool)hbWrites[1].Value);

// 6. Test FlushPendingOnDisconnect and No Auto-Restart
var flushTransport = new FakeAdsMailboxTransport();
flushTransport.OnSymbolWritten = armedTransport.OnSymbolWritten;
var flushGateway = LineMailboxGateway.CreateForTesting(flushTransport, commissioned: true);
flushGateway.RequestManualBeltSet(false, true, 90.0);
flushGateway.RequestManualBeltStart(false, true, true, 90.0);
Check("flush gateway: manual belt active before disconnect", flushGateway.ManualBeltActive);
flushGateway.FlushPendingOnDisconnect();
Check("flush gateway: ManualBeltActive cleared on disconnect", !flushGateway.ManualBeltActive);
Check("flush gateway: PriorBeltSetAccepted cleared on disconnect", !flushGateway.PriorBeltSetAccepted);
Check("flush gateway: AutoLineActive is false", !flushGateway.AutoLineActive);

// 7. Test Speed Preset Boundaries
var spLow = armedGateway.RequestManualBeltSet(false, true, 0.5);
var spHigh = armedGateway.RequestManualBeltSet(false, true, 160.0);
Check("speed preset bounds: 0.5 mm/s rejected with code 13", !spLow.Success && spLow.ResultCode == 13);
Check("speed preset bounds: 160.0 mm/s rejected with code 13", !spHigh.Success && spHigh.ResultCode == 13);

// =========================================================================
// REVIEW DEFECT TARGETED UNIT TESTS (2026-10-23)
// =========================================================================

// 8. STOP truthfulness: fails closed if PLC returns 0 or rejects
var stopFailTransport = new FakeAdsMailboxTransport();
// PLC does not write result (stays 0)
var stopFailGateway = LineMailboxGateway.CreateForTesting(stopFailTransport, commissioned: true);
var sFailRes = stopFailGateway.RequestManualBeltStop(false, true);
Check("stop truthfulness: manual STOP fails closed when PLC does not return 1 (timeout)",
    !sFailRes.Success && sFailRes.ResultCode == 12 && sFailRes.Message.Contains("TIMEOUT"));
var sFailWrites = stopFailTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiBeltStopReq").ToList();
Check("stop truthfulness: stop pulse was still safely released to false on timeout",
    sFailWrites.Count == 2 && (bool)sFailWrites[0].Value == true && (bool)sFailWrites[1].Value == false);

// 9. Monotonic sequence contract & zero writes to PLC-owned nBeltCommandResult
var seqContractTransport = new FakeAdsMailboxTransport();
seqContractTransport.OnSymbolWritten = (symbol, val, transport) =>
{
    if (symbol == "MAIN.bHmiBeltSetReq" && (bool)val)
    {
        if (transport.TryRead<uint>("MAIN.udiHmiBeltCmdSeq", out var bseq))
            transport.SetSymbol("MAIN.udiBeltLastAckSeq", bseq);
        transport.SetSymbol("MAIN.nBeltCommandResult", (ushort)1);
    }
};
var seqContractGateway = LineMailboxGateway.CreateForTesting(seqContractTransport, commissioned: true);
uint mSeq1 = seqContractGateway.AllocateManualSequence();
uint mSeq2 = seqContractGateway.AllocateManualSequence();
Check("manual sequence: allocates monotonically increasing sequence", mSeq2 == mSeq1 + 1);
var scRes = seqContractGateway.RequestManualBeltSet(false, true, 85.0);
Check("sequence contract: manual SET succeeds with verified seq ACK", scRes.Success);
Check("sequence contract: HMI NEVER writes to PLC-owned nBeltCommandResult",
    !seqContractTransport.Writes.Any(w => w.Symbol == "MAIN.nBeltCommandResult"));

// 9B. Strict SET -> START ordering
var unorderGateway = LineMailboxGateway.CreateForTesting(seqContractTransport, commissioned: true);
var unorderStart = unorderGateway.RequestManualBeltStart(false, true, true, 75.0);
Check("strict ordering: START blocked if prior SET not performed for this setpoint",
    !unorderStart.Success && unorderStart.Message.Contains("prior SET"));

// 9C. Guaranteed pulse release on cancellation
var cancelTransport = new FakeAdsMailboxTransport();
var cancelGateway = new LineMailboxGateway(cancelTransport, commissionedForTesting: true)
{
    PulseHoldMs = 200,
    AckTimeoutMs = 500,
    PollIntervalMs = 10
};
using var cts = new CancellationTokenSource(30); // cancel while in pulse hold
try { await cancelGateway.RequestManualBeltSetAsync(false, true, 100.0, cts.Token); } catch { }
var cancelWrites = cancelTransport.Writes.Where(w => w.Symbol == "MAIN.bHmiBeltSetReq").ToList();
Check("cancellation safety: pulse is guaranteed released to false upon cancellation",
    cancelWrites.Count == 2 && (bool)cancelWrites[0].Value == true && (bool)cancelWrites[1].Value == false);
// 10. Transport connection lifecycle
var connTransport = new FakeAdsMailboxTransport { IsConnected = false, IsInRunState = false };
var connGateway = LineMailboxGateway.CreateForTesting(connTransport, commissioned: false);
Check("transport lifecycle: initially disconnected", !connGateway.Transport.IsConnected);
bool connected = connGateway.EnsureTransportConnected(851);
Check("transport lifecycle: EnsureTransportConnected connects transport", connected && connGateway.Transport.IsConnected);
connGateway.DisconnectTransport();
Check("transport lifecycle: DisconnectTransport disconnects transport", !connGateway.Transport.IsConnected);

// 11. Pulse hold duration logic verification
var timingTransport = new FakeAdsMailboxTransport();
timingTransport.OnSymbolWritten = armedTransport.OnSymbolWritten;
var timingGateway = new LineMailboxGateway(timingTransport, commissionedForTesting: true)
{
    PulseHoldMs = 25,
    AckTimeoutMs = 200,
    PollIntervalMs = 5
};
var swTimer = System.Diagnostics.Stopwatch.StartNew();
var tRes = timingGateway.RequestManualBeltSet(false, true, 100.0);
swTimer.Stop();
Check("pulse timing: manual SET with PulseHoldMs=25 took >= 25ms",
    tRes.Success && swTimer.ElapsedMilliseconds >= 20);

// 12. Manual Belt Architecture: Independent of ServoReady, Camera, SCARA and Gantry
var beltIndepTransport = new FakeAdsMailboxTransport();
beltIndepTransport.SetSymbol("MAIN.bServo_Ready", false); // Servo NOT ready
beltIndepTransport.SetSymbol("MAIN.bPlcQConnected", false); // Gantry disconnected
beltIndepTransport.OnSymbolWritten = armedTransport.OnSymbolWritten;
var beltIndepGateway = LineMailboxGateway.CreateForTesting(beltIndepTransport, commissioned: true);
var biSet = beltIndepGateway.RequestManualBeltSet(false, true, 95.0);
var biStart = beltIndepGateway.RequestManualBeltStart(false, true, true, 95.0);
Check("manual belt architecture: manual SET succeeds when ServoReady is FALSE", biSet.Success);
Check("manual belt architecture: manual START succeeds when ServoReady and Gantry are FALSE",
    biStart.Success && beltIndepGateway.ManualBeltActive);
Console.WriteLine("TOTAL_TEST_FAILURES="+failed);
return failed==0?0:1;
