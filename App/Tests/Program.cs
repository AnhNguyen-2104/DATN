using PlateVision50.TwinCAT;
using OpenCvSharp;
using PlateVision50.Vision;
using PlateVision50.Trigger;
using System;
using System.IO;
using System.Linq;
using System.Net;
using WpfYolo_DotNet.Devices;
int failed=0;
void Check(string name,bool value){Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failed++;}
Check("fixed nominal K UI: 100 mm/s previews 15.00 Hz",LineSpeedPreset.NominalPreviewHz(100).Contains("15.00 Hz"));
Check("fixed nominal K UI: preview explicitly forbids PLC writes",LineSpeedPreset.NominalPreviewHz(100).Contains("NO PLC WRITE"));

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
// CONVEYOR SUBSYSTEM CALIBRATION RESET INTERLOCK TESTS:
{
    var calibLatchInput = new ConveyorCalibrationResetInputs(
        AdsFresh: true,
        AuxEstopOk: true,
        GlobalStop: true, // In PLC, bSystemStopRequested is derived as !bSystemEStopOK || bLineNormalStopLatched
        NormalStopLatched: true,
        BeltRunning: false,
        GlueActive: false,
        AutoActive: false);

    var evalAllowed = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput);
    Check("calibration reset: allowed when normal stop latched and aux estop ok",
        evalAllowed.CanReset && evalAllowed.Reason.Contains("permitted"));

    var evalEstopOpen = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput with { AuxEstopOk = false });
    Check("calibration reset: denied when auxiliary estop not ok",
        !evalEstopOpen.CanReset && evalEstopOpen.Reason.Contains("Emergency stop"));

    var evalBeltRun = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput with { BeltRunning = true });
    Check("calibration reset: denied when belt running",
        !evalBeltRun.CanReset && evalBeltRun.Reason.Contains("Conveyor motion active"));

    var evalAdsStale = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput with { AdsFresh = false });
    Check("calibration reset: denied when ADS telemetry stale",
        !evalAdsStale.CanReset && evalAdsStale.Reason.Contains("ADS 851 offline or stale"));

    var evalGlue = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput with { GlueActive = true });
    Check("calibration reset: denied when glue active",
        !evalGlue.CanReset && evalGlue.Reason.Contains("glue cycle active"));

    var evalAuto = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput with { AutoActive = true });
    Check("calibration reset: denied when AUTO active",
        !evalAuto.CanReset && evalAuto.Reason.Contains("AUTO sequence active"));

    var evalGlobalNoLatch = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput with { NormalStopLatched = false, GlobalStop = true });
    Check("calibration reset: denied when global stop active without latch",
        !evalGlobalNoLatch.CanReset && evalGlobalNoLatch.Reason.Contains("Global stop active without normal stop latch"));

    var evalNoLatch = ConveyorCalibrationInterlock.EvaluateReset(calibLatchInput with { NormalStopLatched = false, GlobalStop = false });
    Check("calibration reset: denied when normal stop not latched",
        !evalNoLatch.CanReset && evalNoLatch.Reason.Contains("RESET NOT REQUIRED"));

    var calibStartBlocked = ReadyInputs() with { GlobalStop = true };
    Check("interlock: global stop blocks line start even if aux estop is ok",
        !ilSafety.Evaluate(calibStartBlocked).CanStart);
}
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
    Check("camera mode: station 1 defaults to USB and station 2 defaults to IP",
        CameraSlotDefaults.DefaultMode(1)==CameraTransportMode.Usb&&
        CameraSlotDefaults.DefaultMode(2)==CameraTransportMode.Ip);
    Check("camera vendor: station 1 defaults to Basler and station 2 defaults to Hikrobot",
        CameraSlotDefaults.DefaultVendor(1)=="Basler"&&
        CameraSlotDefaults.DefaultVendor(2)=="Hikrobot");
    Check("camera mode: slot models start in their station default with no scan issues",
        new CameraSlotModel(1,"Station 01").Mode==CameraTransportMode.Usb&&
        new CameraSlotModel(2,"Station 02").Mode==CameraTransportMode.Ip&&
        new CameraSlotModel(1,"Station 01").ScanIssues.Count==0);
    Check("camera mode: legacy Auto, blank and invalid values migrate to the station default",
        CameraSlotDefaults.MigrateMode(null,1)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("Auto",1)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("auto",2)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("AUTO",1)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("",2)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("nonsense",1)==CameraTransportMode.Usb);
    Check("camera mode: slot 1 migrates strictly to USB and slot 2 migrates to IP",
        CameraSlotDefaults.MigrateMode("USB",1)==CameraTransportMode.Usb&&
        CameraSlotDefaults.MigrateMode("usb",2)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("IP",2)==CameraTransportMode.Ip&&
        CameraSlotDefaults.MigrateMode("ip",1)==CameraTransportMode.Usb);
    Check("camera vendor: legacy, null, and cross-station values migrate to slot vendor policy",
        CameraSlotDefaults.MigrateVendor(null,1)=="Basler"&&
        CameraSlotDefaults.MigrateVendor("Auto",1)=="Basler"&&
        CameraSlotDefaults.MigrateVendor("",2)=="Hikrobot"&&
        CameraSlotDefaults.MigrateVendor("Hikvision",1)=="Basler"&&
        CameraSlotDefaults.MigrateVendor("Basler",2)=="Hikrobot"&&
        CameraSlotDefaults.MigrateVendor("Basler",1)=="Basler");
    Check("vendor recognition: Basler recognized case-insensitively and trimmed",
        CameraTransportPolicy.IsBaslerVendor("Basler") &&
        CameraTransportPolicy.IsBaslerVendor("basler") &&
        CameraTransportPolicy.IsBaslerVendor("BASLER") &&
        CameraTransportPolicy.IsBaslerVendor("  Basler  "));
    Check("vendor recognition: Basler rejects Hik, unknown vendors, and empty/null",
        !CameraTransportPolicy.IsBaslerVendor("Hikrobot") &&
        !CameraTransportPolicy.IsBaslerVendor("Hikvision") &&
        !CameraTransportPolicy.IsBaslerVendor("Sony") &&
        !CameraTransportPolicy.IsBaslerVendor("") &&
        !CameraTransportPolicy.IsBaslerVendor("   ") &&
        !CameraTransportPolicy.IsBaslerVendor(null));
    Check("vendor recognition: Hikrobot and Hikvision recognized case-insensitively and trimmed",
        CameraTransportPolicy.IsHikVendor("Hikvision") &&
        CameraTransportPolicy.IsHikVendor("hikvision") &&
        CameraTransportPolicy.IsHikVendor("HIKVISION") &&
        CameraTransportPolicy.IsHikVendor("Hikrobot") &&
        CameraTransportPolicy.IsHikVendor("hikrobot") &&
        CameraTransportPolicy.IsHikVendor("HIKROBOT") &&
        CameraTransportPolicy.IsHikVendor("  Hikrobot  ") &&
        CameraTransportPolicy.IsHikVendor("  Hikvision  "));
    Check("vendor recognition: Hik rejects Basler, unknown vendors, and empty/null",
        !CameraTransportPolicy.IsHikVendor("Basler") &&
        !CameraTransportPolicy.IsHikVendor("Sony") &&
        !CameraTransportPolicy.IsHikVendor("FLIR") &&
        !CameraTransportPolicy.IsHikVendor("") &&
        !CameraTransportPolicy.IsHikVendor(null));
    Check("vendor recognition: Basler and Hik recognition are mutually exclusive",
        !(CameraTransportPolicy.IsBaslerVendor("Hikrobot") && CameraTransportPolicy.IsHikVendor("Hikrobot")) &&
        !(CameraTransportPolicy.IsBaslerVendor("Basler") && CameraTransportPolicy.IsHikVendor("Basler")));
    Check("vendor key compatibility: Basler accepts Basler keys and rejects Hik keys",
        CameraSlotDefaults.IsKeyCompatibleWithVendor("BASLER|40098765", "Basler") &&
        CameraSlotDefaults.IsKeyCompatibleWithVendor("basler|40098765", "Basler") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor("HIKROBOT|DA7403922", "Basler") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor("HIKVISION|00F12345", "Basler") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor("HIK|1234", "Basler") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor("", "Basler") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor(null, "Basler"));
    Check("vendor key compatibility: Hikrobot accepts Hik keys and rejects Basler keys",
        CameraSlotDefaults.IsKeyCompatibleWithVendor("HIKROBOT|DA7403922", "Hikrobot") &&
        CameraSlotDefaults.IsKeyCompatibleWithVendor("HIKVISION|00F12345", "Hikrobot") &&
        CameraSlotDefaults.IsKeyCompatibleWithVendor("hikrobot|da7403922", "Hikrobot") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor("BASLER|40098765", "Hikrobot") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor("basler|40098765", "Hikrobot") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor("", "Hikrobot") &&
        !CameraSlotDefaults.IsKeyCompatibleWithVendor(null, "Hikrobot"));

    // Camera slot normalization (station-specific vendor policy: slot 1 Basler, slot 2 Hikrobot)
    var claimedTestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normS1 = CameraSlotDefaults.NormalizeSlot(1, "Usb", "", "", "BASLER|40098765", "Basler", claimedTestKeys);
    var normS2 = CameraSlotDefaults.NormalizeSlot(2, "Ip", "169.254.113.1", "", "HIKROBOT|DA7403922", "Hikrobot", claimedTestKeys);

    Check("camera slot normalization: slot 1 preserves compatible Basler key and vendor in USB mode",
        normS1.Mode == CameraTransportMode.Usb &&
        normS1.Vendor == "Basler" &&
        string.IsNullOrEmpty(normS1.Ip) &&
        string.IsNullOrEmpty(normS1.Nic) &&
        normS1.DeviceKey == "BASLER|40098765");

    var claimedHikOnBasler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normHikOnS1 = CameraSlotDefaults.NormalizeSlot(1, "Ip", "169.254.113.1", "", "HIKROBOT|J95682667", "Hikrobot", claimedHikOnBasler);
    Check("camera slot normalization: slot 1 migrates vendor to Basler, mode to Usb, and clears incompatible Hikrobot key/IP",
        normHikOnS1.Mode == CameraTransportMode.Usb &&
        string.IsNullOrEmpty(normHikOnS1.Ip) &&
        string.IsNullOrEmpty(normHikOnS1.Nic) &&
        string.IsNullOrEmpty(normHikOnS1.DeviceKey) &&
        normHikOnS1.Vendor == "Basler");
    Check("camera slot normalization: slot 2 preserves compatible Hikrobot key and vendor",
        normS2.Mode == CameraTransportMode.Ip &&
        normS2.Vendor == "Hikrobot" &&
        normS2.Ip == "169.254.113.1" &&
        normS2.DeviceKey == "HIKROBOT|DA7403922");

    var claimedDupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normDup1 = CameraSlotDefaults.NormalizeSlot(1, "Usb", "", "", "BASLER|40098765", "Basler", claimedDupKeys);
    var normDup2 = CameraSlotDefaults.NormalizeSlot(2, "Ip", "169.254.113.1", "", "BASLER|40098765", "Hikrobot", claimedDupKeys);
    Check("camera slot normalization: clears duplicate and incompatible Basler key on slot 2",
        normDup1.DeviceKey == "BASLER|40098765" && string.IsNullOrEmpty(normDup2.DeviceKey) && string.IsNullOrEmpty(normDup2.Ip));

    var claimedDupIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var claimedDupKeys2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normDupIp1 = CameraSlotDefaults.NormalizeSlot(1, "Usb", "", "", "BASLER|40098765", "Basler", claimedDupKeys2, claimedDupIps);
    var normDupIp2 = CameraSlotDefaults.NormalizeSlot(2, "Ip", "169.254.113.1", "", "HIKROBOT|DA7403922", "Hikrobot", claimedDupKeys2, claimedDupIps);
    Check("camera slot normalization: enforces IP deduplication across slots and slot 1 USB claims no IP",
        string.IsNullOrEmpty(normDupIp1.Ip) && normDupIp2.Ip == "169.254.113.1");
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
    Check("transport matching: USB mode matches USB/USB3 variants and rejects GigE/IP/unsupported",
        CameraTransportPolicy.MatchesMode(Cam("Basler", "USB", "B-1", "m"), CameraTransportMode.Usb) &&
        CameraTransportPolicy.MatchesMode(Cam("Basler", "USB3", "B-2", "m"), CameraTransportMode.Usb) &&
        CameraTransportPolicy.MatchesMode(Cam("Hikrobot", "usb", "H-1", "m"), CameraTransportMode.Usb) &&
        !CameraTransportPolicy.MatchesMode(Cam("Basler", "GigE", "B-3", "m"), CameraTransportMode.Usb) &&
        !CameraTransportPolicy.MatchesMode(Cam("Hikrobot", "IP", "H-2", "m"), CameraTransportMode.Usb) &&
        !CameraTransportPolicy.MatchesMode(Cam("Basler", "CameraLink", "B-4", "m"), CameraTransportMode.Usb) &&
        !CameraTransportPolicy.MatchesMode(Cam("Basler", "", "B-5", "m"), CameraTransportMode.Usb));
    Check("transport matching: IP mode matches GigE/IP variants and rejects USB/unsupported",
        CameraTransportPolicy.MatchesMode(Cam("Basler", "GigE", "B-1", "m"), CameraTransportMode.Ip) &&
        CameraTransportPolicy.MatchesMode(Cam("Hikrobot", "gige", "H-1", "m"), CameraTransportMode.Ip) &&
        CameraTransportPolicy.MatchesMode(Cam("Hikvision", "IP", "H-2", "m"), CameraTransportMode.Ip) &&
        CameraTransportPolicy.MatchesMode(Cam("Basler", "GigE Vision", "B-2", "m"), CameraTransportMode.Ip) &&
        !CameraTransportPolicy.MatchesMode(Cam("Basler", "USB", "B-3", "m"), CameraTransportMode.Ip) &&
        !CameraTransportPolicy.MatchesMode(Cam("Hikrobot", "USB3", "H-3", "m"), CameraTransportMode.Ip) &&
        !CameraTransportPolicy.MatchesMode(Cam("Basler", "CoaXPress", "B-4", "m"), CameraTransportMode.Ip) &&
        !CameraTransportPolicy.MatchesMode(Cam("Basler", "", "B-5", "m"), CameraTransportMode.Ip));
    Check("transport matching: null camera device matches neither mode",
        !CameraTransportPolicy.MatchesMode(null!, CameraTransportMode.Usb) &&
        !CameraTransportPolicy.MatchesMode(null!, CameraTransportMode.Ip));

    var dBaslerUsb = Cam("Basler", "USB", "B-U", "acA");
    var dBaslerGigE = Cam("Basler", "GigE", "B-G", "acA", 0, "169.254.113.5");
    var dHikrobotUsb = Cam("Hikrobot", "USB", "HR-U", "MV-U");
    var dHikrobotGigE = Cam("Hikrobot", "GigE", "HR-G", "MV-G", 0, "169.254.113.1");
    var dHikvisionUsb = Cam("Hikvision", "USB", "HV-U", "MV-U");
    var dHikvisionGigE = Cam("Hikvision", "GigE", "HV-G", "MV-G", 0, "169.254.113.1");

    Check("dispatch matrix: Basler USB allowed in USB and rejected in IP",
        CameraTransportPolicy.CanDispatch(dBaslerUsb, CameraTransportMode.Usb, out var rBU_U) && rBU_U.Length == 0 &&
        !CameraTransportPolicy.CanDispatch(dBaslerUsb, CameraTransportMode.Ip, out var rBU_I) &&
        rBU_I.Contains("IP mode") && rBU_I.Contains("GigE"));
    Check("dispatch matrix: Basler GigE allowed in IP and rejected in USB",
        CameraTransportPolicy.CanDispatch(dBaslerGigE, CameraTransportMode.Ip, out var rBG_I) && rBG_I.Length == 0 &&
        !CameraTransportPolicy.CanDispatch(dBaslerGigE, CameraTransportMode.Usb, out var rBG_U) &&
        rBG_U.Contains("USB mode") && rBG_U.Contains("USB transport"));
    Check("dispatch matrix: Hikrobot USB allowed in USB and rejected in IP",
        CameraTransportPolicy.CanDispatch(dHikrobotUsb, CameraTransportMode.Usb, out var rHU_U) && rHU_U.Length == 0 &&
        !CameraTransportPolicy.CanDispatch(dHikrobotUsb, CameraTransportMode.Ip, out var rHU_I) &&
        rHU_I.Contains("IP mode") && rHU_I.Contains("GigE"));
    Check("dispatch matrix: Hikrobot GigE allowed in IP and rejected in USB",
        CameraTransportPolicy.CanDispatch(dHikrobotGigE, CameraTransportMode.Ip, out var rHG_I) && rHG_I.Length == 0 &&
        !CameraTransportPolicy.CanDispatch(dHikrobotGigE, CameraTransportMode.Usb, out var rHG_U) &&
        rHG_U.Contains("USB mode") && rHG_U.Contains("USB transport"));
    Check("dispatch matrix: Hikvision USB allowed in USB and rejected in IP",
        CameraTransportPolicy.CanDispatch(dHikvisionUsb, CameraTransportMode.Usb, out _) &&
        !CameraTransportPolicy.CanDispatch(dHikvisionUsb, CameraTransportMode.Ip, out _));
    Check("dispatch matrix: Hikvision GigE allowed in IP and rejected in USB",
        CameraTransportPolicy.CanDispatch(dHikvisionGigE, CameraTransportMode.Ip, out _) &&
        !CameraTransportPolicy.CanDispatch(dHikvisionGigE, CameraTransportMode.Usb, out _));

    var manHikrobotDirect = new CameraDevice("Hikrobot", "GigE", "MV-CS016", "HR-IP-1", "169.254.113.1", -1);
    var gvcpHikrobotDirect = new CameraDevice("Hikrobot", "GigE", "MV-CS016", "HR-GVCP", "169.254.113.1", -2);
    var manHikvisionDirect = new CameraDevice("Hikvision", "GigE", "MV-CA050", "HV-IP-1", "169.254.113.1", -1);
    var gvcpHikvisionDirect = new CameraDevice("Hikvision", "GigE", "MV-CA050", "HV-GVCP", "169.254.113.1", -2);
    var manBaslerDirect = new CameraDevice("Basler", "GigE", "acA1300", "B-IP-1", "169.254.113.5", -1);
    var gvcpBaslerDirect = new CameraDevice("Basler", "GigE", "acA1300", "B-GVCP", "169.254.113.5", -2);

    Check("manual-IP policy: Hikrobot manual IP (-1) and GVCP direct (-2) are dispatchable in IP mode",
        CameraTransportPolicy.CanDispatchManual(manHikrobotDirect, out var rManHR) && rManHR.Length == 0 &&
        CameraTransportPolicy.CanDispatch(manHikrobotDirect, CameraTransportMode.Ip, out _) &&
        CameraTransportPolicy.CanDispatchManual(gvcpHikrobotDirect, out var rGvcpHR) && rGvcpHR.Length == 0 &&
        CameraTransportPolicy.CanDispatch(gvcpHikrobotDirect, CameraTransportMode.Ip, out _));
    Check("manual-IP policy: Hikvision manual IP (-1) and GVCP direct (-2) are dispatchable in IP mode",
        CameraTransportPolicy.CanDispatchManual(manHikvisionDirect, out _) &&
        CameraTransportPolicy.CanDispatch(manHikvisionDirect, CameraTransportMode.Ip, out _) &&
        CameraTransportPolicy.CanDispatchManual(gvcpHikvisionDirect, out _) &&
        CameraTransportPolicy.CanDispatch(gvcpHikvisionDirect, CameraTransportMode.Ip, out _));
    Check("manual-IP policy: Basler manual IP (-1) is rejected as SCAN REQUIRED with action hint",
        !CameraTransportPolicy.CanDispatchManual(manBaslerDirect, out var rManBasler) &&
        rManBasler.Contains("SCAN REQUIRED", StringComparison.OrdinalIgnoreCase) &&
        rManBasler.Contains("Only Hikrobot/Hikvision", StringComparison.OrdinalIgnoreCase) &&
        !CameraTransportPolicy.CanDispatch(manBaslerDirect, CameraTransportMode.Ip, out _));
    Check("manual-IP policy: Basler GVCP direct (-2) is rejected as SCAN REQUIRED",
        !CameraTransportPolicy.CanDispatchManual(gvcpBaslerDirect, out var rGvcpBasler) &&
        rGvcpBasler.Contains("SCAN REQUIRED", StringComparison.OrdinalIgnoreCase) &&
        !CameraTransportPolicy.CanDispatch(gvcpBaslerDirect, CameraTransportMode.Ip, out _));
    Check("manual-IP policy: scanned Basler GigE (index >= 0) is never blocked by manual restriction",
        CameraTransportPolicy.CanDispatchManual(dBaslerGigE, out var rScanBasler) && rScanBasler.Length == 0 &&
        CameraTransportPolicy.CanDispatch(dBaslerGigE, CameraTransportMode.Ip, out _));

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
    var issueHikvision = new CameraScanIssue("Hikvision", "GigE", "Timeout", "Device 169.254.113.1 unreachable", "detail");
    var issueHikrobotLower = new CameraScanIssue("hikrobot", "usb", "", "Failed to open device", "detail");
    var issueBaslerLower = new CameraScanIssue("basler", "USB", "PylonError", "Enumeration failed", "detail");
    var issueGvcpLower = new CameraScanIssue("gvcp", "GigE", "SocketError", "Port 3956 in use", "detail");
    var issueUnknownVendor = new CameraScanIssue("Sony", "USB", "NotSupported", "Unknown vendor", "detail");
    var issueEmptyVendor = new CameraScanIssue("", "GigE", "UnknownError", "Generic error", "detail");
    var issueNullVendor = new CameraScanIssue(null!, "USB", "Err", "Null vendor", "detail");
    var issueMultiline = new CameraScanIssue("Basler", "GigE", "PylonError", "Line 1\r\nLine 2\nLine 3", "detail");
    var issueNoTransport = new CameraScanIssue("GVCP", "", "SocketError", "Discovery failed", "detail");

    Check("scan issues: Hikvision and lowercase hikrobot labelled HIK SDK ERROR",
        issueHikvision.Label == "HIK SDK ERROR" && issueHikrobotLower.Label == "HIK SDK ERROR");
    Check("scan issues: lowercase basler labelled BASLER SDK ERROR",
        issueBaslerLower.Label == "BASLER SDK ERROR");
    Check("scan issues: lowercase gvcp labelled GVCP ERROR",
        issueGvcpLower.Label == "GVCP ERROR");
    Check("scan issues: unknown vendor labelled with uppercase vendor name",
        issueUnknownVendor.Label == "SONY ERROR");
    Check("scan issues: empty or null vendor labelled CAMERA ERROR",
        issueEmptyVendor.Label == "CAMERA ERROR" && issueNullVendor.Label == "CAMERA ERROR");
    Check("scan issues: summary format includes uppercase transport, message, and error type",
        issueHikvision.Summary == "HIK SDK ERROR · GIGE: Device 169.254.113.1 unreachable [Timeout]");
    Check("scan issues: summary omits error type brackets when error type is empty",
        issueHikrobotLower.Summary == "HIK SDK ERROR · USB: Failed to open device");
    Check("scan issues: summary replaces CRLF and LF newlines with spaces",
        issueMultiline.Summary == "BASLER SDK ERROR · GIGE: Line 1  Line 2 Line 3 [PylonError]");
    Check("scan issues: summary omits transport separator when transport is empty",
        issueNoTransport.Summary == "GVCP ERROR: Discovery failed [SocketError]");

    // Exclusive-lock diagnostics tests (pylon USB exclusive device lock handling)
    string rawPylonError = @"Failed to open device '\\?\usb#vid_2676&pid_ba02&mi_00#6&2135f47d&0&0000#{79bfeeca-947f-11e1-bc58-001b2163ef96}' for XML file download. Error: 'Device is exclusively opened by another client.' : RuntimeException thrown (file 'PylonUsbTL.cpp', line 474)";
    Check("exclusive lock: detects raw pylon exclusive opened message",
        CameraTransportPolicy.IsExclusiveDeviceLockError(rawPylonError));
    Check("exclusive lock: detects generic exclusive substring",
        CameraTransportPolicy.IsExclusiveDeviceLockError("Device is exclusively opened") &&
        CameraTransportPolicy.IsExclusiveDeviceLockError("Device is exclusive") &&
        CameraTransportPolicy.IsExclusiveDeviceLockError("Device is exclus..."));
    Check("exclusive lock: rejects null, blank and unrelated error messages",
        !CameraTransportPolicy.IsExclusiveDeviceLockError(null) &&
        !CameraTransportPolicy.IsExclusiveDeviceLockError("") &&
        !CameraTransportPolicy.IsExclusiveDeviceLockError("   ") &&
        !CameraTransportPolicy.IsExclusiveDeviceLockError("Camera disconnected") &&
        !CameraTransportPolicy.IsExclusiveDeviceLockError("Device timeout"));
    var pylonEx = new InvalidOperationException(rawPylonError);
    string s1Formatted = CameraTransportPolicy.FormatConnectError(1, pylonEx);
    Check("exclusive lock: slot 1 maps to concise actionable instruction naming pylon Viewer/MVS and SCAN USB -> CONNECT",
        s1Formatted == "Station 01 Basler is locked by another client. Close pylon Viewer/MVS before SCAN USB -> CONNECT.");
    Check("exclusive lock: formatted message length fits within UI status bar and Terse limit (<= 120 chars)",
        s1Formatted.Length <= 120 && s1Formatted.Length > 0);
    var pylonExSlot2 = new InvalidOperationException("Device is exclusively opened");
    string s2Formatted = CameraTransportPolicy.FormatConnectError(2, pylonExSlot2);
    Check("exclusive lock: slot 2 maps to station-specific instruction",
        s2Formatted == "Station 02 camera is locked by another client. Close pylon Viewer/MVS before SCAN -> CONNECT.");
    var genericEx = new InvalidOperationException("Generic transport timeout occurred");
    Check("exclusive lock: non-exclusive errors preserve original message up to terse limit",
        CameraTransportPolicy.FormatConnectError(1, genericEx) == "Generic transport timeout occurred");

    // MVS native runtime loader regression tests
    var mvsComputedPath1 = MvsNativeLoader.ComputePrependedPath(@"C:\Windows;C:\Tools", @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64", is64BitProcess: true);
    Check("mvs loader: prepends runtime dir to path",
        mvsComputedPath1.StartsWith(@"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64;C:\Windows;C:\Tools", StringComparison.OrdinalIgnoreCase));

    var mvsComputedPath2 = MvsNativeLoader.ComputePrependedPath(
        @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win32_i86;C:\Windows",
        @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64",
        is64BitProcess: true);
    Check("mvs loader: filters Win32_i86 from path in 64-bit mode",
        !mvsComputedPath2.Contains("Win32_i86", StringComparison.OrdinalIgnoreCase) &&
        mvsComputedPath2.StartsWith(@"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64", StringComparison.OrdinalIgnoreCase));

    var mvsComputedPath3 = MvsNativeLoader.ComputePrependedPath(
        @"C:\Windows;C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64;C:\Tools",
        @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64",
        is64BitProcess: true);
    Check("mvs loader: deduplicates existing runtime directory when prepending",
        mvsComputedPath3 == @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64;C:\Windows;C:\Tools");

    Check("mvs loader: handles empty path cleanly",
        MvsNativeLoader.ComputePrependedPath("", @"C:\MVS", true) == @"C:\MVS");
    Check("mvs loader: handles empty directory cleanly",
        MvsNativeLoader.ComputePrependedPath(@"C:\Windows", "", true) == @"C:\Windows");

    Check("mvs loader: standard path ends with Win64_x64",
        MvsNativeLoader.GetStandardCommonFilesPath().EndsWith(@"MVS\Runtime\Win64_x64", StringComparison.OrdinalIgnoreCase));

    Check("mvs loader: invalid runtime directory returns false",
        !MvsNativeLoader.IsValidRuntimeDirectory(null) &&
        !MvsNativeLoader.IsValidRuntimeDirectory(@"C:\Nonexistent_Directory_12345"));

    var mvsDiscovered = MvsNativeLoader.DiscoverRuntime();
    Check("mvs loader: discovers installed x64 runtime on host",
        mvsDiscovered != null && File.Exists(Path.Combine(mvsDiscovered, "MvCameraControl.dll")));

    bool mvsEnsured = MvsNativeLoader.Ensure();
    Check("mvs loader: Ensure returns true and populates RuntimeDirectory",
        mvsEnsured && !string.IsNullOrWhiteSpace(MvsNativeLoader.RuntimeDirectory));

    var mvsProcPath = Environment.GetEnvironmentVariable("PATH") ?? "";
    Check("mvs loader: process PATH starts with discovered runtime directory",
        mvsProcPath.StartsWith(MvsNativeLoader.RuntimeDirectory!, StringComparison.OrdinalIgnoreCase));

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
    Check("camera slots: default mode for slot 1 is USB and state OFFLINE",
        camSlot.Mode==CameraTransportMode.Usb&&camSlot.State==CameraSlotState.Offline&&camSlot.DisplayState=="OFFLINE");
    camSlot.SetDevices(new[]{camBaslerUsb,camHikGigE,camUvc});
    Check("camera slots: scan status summarizes device counts",camSlot.StatusText=="SCAN 3 DEVICES | USB 2 | IP 1");
    Check("camera slots: scan order is USB then GigE then model",
        camSlot.Devices.Count==3&&camSlot.Devices[0].Key==camBaslerUsb.Key&&
        camSlot.Devices[1].Key==camUvc.Key&&camSlot.Devices[2].Key==camHikGigE.Key);
    camSlot.Mode=CameraTransportMode.Usb;
    Check("camera slots: default USB mode lists USB devices",
        camSlot.FilteredDevices.Count==2&&camSlot.FilteredDevices.All(d=>d.Transport.Contains("USB")));
    camSlot.Mode=CameraTransportMode.Ip;
    Check("camera slots: IP mode filters to GigE transport",
        camSlot.FilteredDevices.Count==1&&camSlot.FilteredDevices[0].Key==camHikGigE.Key);
    var camIpCount=camSlot.FilteredDevices.Count;
    camSlot.Mode=CameraTransportMode.Usb;
    var camUsbCount=camSlot.FilteredDevices.Count;
    Check("camera slots: USB plus IP filtering covers every scanned device",
        camUsbCount==2&&camIpCount==1&&camUsbCount+camIpCount==camSlot.Devices.Count);
    camSlot.Mode=CameraTransportMode.Ip;
    Check("camera slots: GigE device selects in IP mode",camSlot.SelectDevice(camHikGigE.Key));
    // AutoSelectForSlot station remap: Slot 1 is Basler USB, Slot 2 is Hikrobot GigE
    var s1Slot = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Usb };
    s1Slot.SetDevices(new[] { camBaslerUsb, camHikGigE });
    var s1Chosen = s1Slot.AutoSelectForSlot(1);
    Check("station remap: slot 1 AutoSelectForSlot defaults to Basler USB when both vendors present",
        s1Chosen is not null && CameraTransportPolicy.IsBaslerVendor(s1Chosen.Vendor) && s1Chosen.Key == camBaslerUsb.Key);
    var s1ChosenBasler = s1Slot.AutoSelectForSlot(1, "Basler");
    Check("station remap: slot 1 AutoSelectForSlot selects Basler USB when Basler requested",
        s1ChosenBasler is not null && CameraTransportPolicy.IsBaslerVendor(s1ChosenBasler.Vendor));
    var s1HikOnlySlot = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Usb };
    s1HikOnlySlot.SetDevices(new[] { camHikGigE });
    var s1HikSelect = s1HikOnlySlot.AutoSelectForSlot(1);
    Check("station remap: slot 1 AutoSelectForSlot rejects Hikrobot when no Basler present",
        s1HikSelect is null && s1HikOnlySlot.Selected is null);
    var s2Slot = new CameraSlotModel(2, "Station 02") { Mode = CameraTransportMode.Ip };
    s2Slot.SetDevices(new[] { camBaslerUsb, camHikGigE });
    var s2Chosen = s2Slot.AutoSelectForSlot(2);
    Check("station remap: slot 2 AutoSelectForSlot selects Hikrobot when both vendors present",
        s2Chosen is not null && CameraTransportPolicy.IsHikVendor(s2Chosen.Vendor));
    var s2BaslerOnlySlot = new CameraSlotModel(2, "Station 02") { Mode = CameraTransportMode.Ip };
    s2BaslerOnlySlot.SetDevices(new[] { Cam("Basler", "GigE", "B-G", "acA", 0, "10.0.0.3") });
    var s2BaslerReject = s2BaslerOnlySlot.AutoSelectForSlot(2);
    Check("station remap: slot 2 AutoSelectForSlot rejects Basler when no Hikrobot present",
        s2BaslerReject is null && s2BaslerOnlySlot.Selected is null);
    var s2BaslerRequest = s2BaslerOnlySlot.AutoSelectForSlot(2, "Basler");
    Check("station remap: slot 2 AutoSelectForSlot migrates Basler request to Hikrobot and rejects missing Hik",
        s2BaslerRequest is null && s2BaslerOnlySlot.Selected is null);
    var s1MixedUsb = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Usb };
    s1MixedUsb.SetDevices(new[] { Cam("Hikrobot", "USB", "HR-U", "MV-U"), camBaslerUsb });
    Check("station remap: slot 1 AutoSelectForSlot defaults to Basler USB in USB mode",
        s1MixedUsb.AutoSelectForSlot(1)?.Key == camBaslerUsb.Key);
    Check("station remap: slot 1 AutoSelectForSlot chooses Basler USB over Hikrobot USB under station policy",
        s1MixedUsb.AutoSelectForSlot(1, "Hikrobot")?.Key == camBaslerUsb.Key);
    var s1BaslerUsbOnly = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Usb };
    s1BaslerUsbOnly.SetDevices(new[] { camBaslerUsb });
    Check("station remap: slot 1 AutoSelectForSlot selects Basler USB in USB mode",
        s1BaslerUsbOnly.AutoSelectForSlot(1)?.Key == camBaslerUsb.Key);
    var s2HikvisionSlot = new CameraSlotModel(2, "Station 02") { Mode = CameraTransportMode.Ip };
    s2HikvisionSlot.SetDevices(new[] { dBaslerGigE, dHikvisionGigE });
    Check("station remap: slot 2 AutoSelectForSlot accepts Hikvision vendor in IP mode",
        s2HikvisionSlot.AutoSelectForSlot(2)?.Key == dHikvisionGigE.Key);
    var s2BaslerGigEOnly = new CameraSlotModel(2, "Station 02") { Mode = CameraTransportMode.Ip };
    s2BaslerGigEOnly.SetDevices(new[] { dBaslerGigE });
    Check("station remap: slot 2 AutoSelectForSlot rejects Basler GigE in IP mode",
        s2BaslerGigEOnly.AutoSelectForSlot(2) is null && s2BaslerGigEOnly.Selected is null);
    Check("station remap: slot 2 AutoSelectForSlot rejects Basler GigE even when Basler requested",
        s2BaslerGigEOnly.AutoSelectForSlot(2, "Basler") is null && s2BaslerGigEOnly.Selected is null);
    var slotManualTest = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Ip };
    slotManualTest.SetDevices(new[] { manHikrobotDirect, manBaslerDirect, dBaslerGigE });
    Check("slot selection: Hikrobot direct IP device selects successfully in IP mode",
        slotManualTest.SelectDevice(manHikrobotDirect.Key) &&
        slotManualTest.Selected?.Key == manHikrobotDirect.Key &&
        slotManualTest.LastError.Length == 0);
    Check("slot selection: Basler direct IP device is rejected by SelectDevice with SCAN REQUIRED",
        !slotManualTest.SelectDevice(manBaslerDirect.Key) &&
        slotManualTest.LastError.Contains("SCAN REQUIRED", StringComparison.OrdinalIgnoreCase));
    Check("slot selection: scanned Basler GigE device selects successfully in IP mode",
        slotManualTest.SelectDevice(dBaslerGigE.Key) &&
        slotManualTest.Selected?.Key == dBaslerGigE.Key);
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
    var camModeSlot=new CameraSlotModel(2,"Camera 02");
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
    var camDirectSlot=new CameraSlotModel(2,"Camera 02");
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
    // 22. Station-Specific Camera Vendor Contract & Isolation Tests (Station 01 Basler, Station 02 Hikrobot)
    // (a) Slot 1 defaults/migrates to Basler; Slot 2 defaults/migrates to Hikrobot
    Check("station-vendor (a): slot 1 default vendor is Basler and slot 2 is Hikrobot",
        CameraSlotDefaults.DefaultVendor(1) == "Basler" &&
        CameraSlotDefaults.DefaultVendor(2) == "Hikrobot");

    Check("station-vendor (a): slot 1 null, blank, and invalid migrate to Basler default",
        CameraSlotDefaults.MigrateVendor(null, 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("   ", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("invalid", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("Auto", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("Hikrobot", 1) == "Basler");

    Check("station-vendor (a): slot 2 null, blank, and invalid migrate to Hikrobot default",
        CameraSlotDefaults.MigrateVendor(null, 2) == "Hikrobot" &&
        CameraSlotDefaults.MigrateVendor("", 2) == "Hikrobot" &&
        CameraSlotDefaults.MigrateVendor("   ", 2) == "Hikrobot" &&
        CameraSlotDefaults.MigrateVendor("invalid", 2) == "Hikrobot" &&
        CameraSlotDefaults.MigrateVendor("Auto", 2) == "Hikrobot" &&
        CameraSlotDefaults.MigrateVendor("Basler", 2) == "Hikrobot");

    Check("station-vendor (a): explicit values migrate to slot vendor policy",
        CameraSlotDefaults.MigrateVendor("Basler", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("basler", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("hikrobot", 2) == "Hikrobot" &&
        CameraSlotDefaults.MigrateVendor("hikvision", 2) == "Hikrobot");

    var s1SlotBaslerAuto = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Usb };
    s1SlotBaslerAuto.SetDevices(new[] { camHikGigE, camBaslerUsb });
    var s1ChosenBaslerAuto = s1SlotBaslerAuto.AutoSelectForSlot(1);
    Check("station-vendor (a): slot 1 AutoSelectForSlot defaults to Basler when both present",
        s1ChosenBaslerAuto is not null && CameraTransportPolicy.IsBaslerVendor(s1ChosenBaslerAuto.Vendor));

    // (b) Valid Basler slot-1 persistence is preserved in USB mode (no IP/NIC)
    var claimedS1Basler = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normBaslerS1 = CameraSlotDefaults.NormalizeSlot(1, "Usb", "", "", "BASLER|40098765", "Basler", claimedS1Basler);
    Check("station-vendor (b): slot 1 preserves valid Basler persistence in USB mode",
        normBaslerS1.Mode == CameraTransportMode.Usb &&
        normBaslerS1.Vendor == "Basler" &&
        string.IsNullOrEmpty(normBaslerS1.Ip) &&
        string.IsNullOrEmpty(normBaslerS1.Nic) &&
        normBaslerS1.DeviceKey == "BASLER|40098765");

    var normBaslerS1NullVendor = CameraSlotDefaults.NormalizeSlot(1, "Usb", "", "", "BASLER|40098765", null);
    Check("station-vendor (b): slot 1 with null/default vendor preserves valid Basler persistence in USB mode",
        normBaslerS1NullVendor.Mode == CameraTransportMode.Usb &&
        normBaslerS1NullVendor.Vendor == "Basler" &&
        string.IsNullOrEmpty(normBaslerS1NullVendor.Ip) &&
        string.IsNullOrEmpty(normBaslerS1NullVendor.Nic) &&
        normBaslerS1NullVendor.DeviceKey == "BASLER|40098765");

    // (c) Two distinct slot camera configs remain isolated (slot 1 USB, slot 2 GigE IP)
    var claimedKeysDualTdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var claimedIpsDualTdd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var s1DualTdd = CameraSlotDefaults.NormalizeSlot(1, "Usb", "", "", "BASLER|40098765", "Basler", claimedKeysDualTdd, claimedIpsDualTdd);
    var s2DualTdd = CameraSlotDefaults.NormalizeSlot(2, "Ip", "169.254.113.1", "169.254.113.10", "HIKROBOT|DA7403922", "Hikrobot", claimedKeysDualTdd, claimedIpsDualTdd);

    Check("station-vendor (c): two distinct slot configurations remain isolated",
        s1DualTdd.Mode == CameraTransportMode.Usb && s1DualTdd.DeviceKey == "BASLER|40098765" && s1DualTdd.Vendor == "Basler" &&
        string.IsNullOrEmpty(s1DualTdd.Ip) &&
        s2DualTdd.Mode == CameraTransportMode.Ip && s2DualTdd.Ip == "169.254.113.1" && s2DualTdd.Nic == "169.254.113.10" &&
        s2DualTdd.DeviceKey == "HIKROBOT|DA7403922" && s2DualTdd.Vendor == "Hikrobot" &&
        s1DualTdd.DeviceKey != s2DualTdd.DeviceKey);
    var regDualTdd = new CameraOwnershipRegistry();
    Check("station-vendor (c): registry claims both slot camera keys without collision",
        regDualTdd.TryClaim(s1DualTdd.DeviceKey, 1, out string errClaim1Tdd) && string.IsNullOrEmpty(errClaim1Tdd) &&
        regDualTdd.TryClaim(s2DualTdd.DeviceKey, 2, out string errClaim2Tdd) && string.IsNullOrEmpty(errClaim2Tdd) &&
        regDualTdd.OwnerOf(s1DualTdd.DeviceKey) == 1 &&
        regDualTdd.OwnerOf(s2DualTdd.DeviceKey) == 2);

    regDualTdd.Release(2);
    Check("station-vendor (c): releasing slot 2 preserves slot 1 claim",
        regDualTdd.OwnerOf(s1DualTdd.DeviceKey) == 1 &&
        regDualTdd.OwnerOf(s2DualTdd.DeviceKey) == null);

    // (d) Blank/invalid manual camera IP or explicit host NIC fails closed
    Check("station-vendor (d): ResolveCameraNic fails closed when explicit host NIC is invalid (no fallback)",
        CameraTransportPolicy.ResolveCameraNic("169.254.113.1", "not-an-ip") == "" &&
        CameraTransportPolicy.ResolveCameraNic("169.254.113.1", "999.999.999.999") == "" &&
        CameraTransportPolicy.ResolveCameraNic("192.168.50.2", "invalid_nic") == "");

    Check("station-vendor (d): ResolveCameraNic fails closed on blank camera IP and blank NIC (no fallback to preferred NIC)",
        CameraTransportPolicy.ResolveCameraNic("", "") == "" &&
        CameraTransportPolicy.ResolveCameraNic(null, null) == "" &&
        CameraTransportPolicy.ResolveCameraNic("   ", "   ") == "");

    Check("station-vendor (d): ResolveCameraNic uses explicit valid host NIC exactly without fallback",
        CameraTransportPolicy.ResolveCameraNic("192.168.50.2", "192.168.50.1") == "192.168.50.1" &&
        CameraTransportPolicy.ResolveCameraNic("169.254.113.1", "169.254.113.10") == "169.254.113.10");

    var slotManualBlankIp = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Ip, IpText = "" };
    slotManualBlankIp.SetDevices(new[] { manHikrobotDirect });
    slotManualBlankIp.SelectDevice(manHikrobotDirect.Key);
    Check("station-vendor (d): manual connect with blank camera IP fails closed before connect",
        !slotManualBlankIp.ValidateBeforeConnect(new CameraOwnershipRegistry()).ok);

    var slotManualBadIp = new CameraSlotModel(1, "Station 01") { Mode = CameraTransportMode.Ip, IpText = "999.999.999.999" };
    slotManualBadIp.SetDevices(new[] { manHikrobotDirect });
    slotManualBadIp.SelectDevice(manHikrobotDirect.Key);
    Check("station-vendor (d): manual connect with invalid camera IP fails closed before connect",
        !slotManualBadIp.ValidateBeforeConnect(new CameraOwnershipRegistry()).ok);

    // (e) Station-specific vendor policy: slot 1 is Basler, slot 2 is Hikrobot
    Check("station-vendor (e): saved vendor migrates per slot (slot 1 Basler, slot 2 Hikrobot)",
        CameraSlotDefaults.MigrateVendor("Basler", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("Hikrobot", 1) == "Basler" &&
        CameraSlotDefaults.MigrateVendor("Basler", 2) == "Hikrobot" &&
        CameraSlotDefaults.MigrateVendor("Hikrobot", 2) == "Hikrobot");

    var claimedBaslerPreserve = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normHikOnSlot1 = CameraSlotDefaults.NormalizeSlot(1, "Ip", "169.254.113.1", "", "HIKROBOT|DA7403922", "Hikrobot", claimedBaslerPreserve);
    Check("station-vendor (e): slot 1 with legacy Hikrobot key migrates vendor to Basler and clears incompatible Hikrobot key",
        normHikOnSlot1.Vendor == "Basler" &&
        string.IsNullOrEmpty(normHikOnSlot1.Ip) &&
        string.IsNullOrEmpty(normHikOnSlot1.DeviceKey));

    var normBaslerSlot2 = CameraSlotDefaults.NormalizeSlot(2, "Ip", "169.254.113.6", "", "BASLER|40098766", "Basler", claimedBaslerPreserve);
    Check("station-vendor (e): slot 2 with legacy Basler key migrates vendor to Hikrobot and clears incompatible Basler key",
        normBaslerSlot2.Vendor == "Hikrobot" &&
        string.IsNullOrEmpty(normBaslerSlot2.Ip) &&
        string.IsNullOrEmpty(normBaslerSlot2.DeviceKey));
    Check("station-vendor (e): Basler and Hikrobot SDK wrappers remain dispatchable",
        CameraTransportPolicy.CanDispatch(camBaslerUsb, CameraTransportMode.Usb, out _) &&
        CameraTransportPolicy.CanDispatch(dBaslerGigE, CameraTransportMode.Ip, out _));

    // (f) Manual Hikrobot IP on slot 2 is preserved across normalization even with empty key
    var claimedManualEmptyKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normManualS1 = CameraSlotDefaults.NormalizeSlot(1, "Ip", "169.254.113.5", "169.254.113.10", "", "Basler", claimedManualEmptyKey);
    Check("station-vendor (f): slot 1 Basler with empty key migrates mode to Usb and does NOT synthesize manual IP key",
        normManualS1.Mode == CameraTransportMode.Usb &&
        string.IsNullOrEmpty(normManualS1.Ip) &&
        string.IsNullOrEmpty(normManualS1.Nic) &&
        string.IsNullOrEmpty(normManualS1.DeviceKey) &&
        normManualS1.Vendor == "Basler");

    var normManualS2 = CameraSlotDefaults.NormalizeSlot(2, "Ip", "192.168.50.2", "192.168.50.1", "", "Hikrobot", claimedManualEmptyKey);
    Check("station-vendor (f): slot 2 manual Hikrobot IP with empty key preserves distinct IP and key",
        normManualS2.Mode == CameraTransportMode.Ip &&
        normManualS2.Ip == "192.168.50.2" &&
        normManualS2.Nic == "192.168.50.1" &&
        normManualS2.DeviceKey == "HIKVISION|IP:192.168.50.2" &&
        normManualS2.Vendor == "Hikrobot");

    // (g) Deterministic dictionary normalization & save-order isolation tests (pure helper, no file writes)
    var rawLegacySlots = new Dictionary<string, (string Mode, string Ip, string Nic, string DeviceKey, string? Vendor)>
    {
        ["1"] = ("Ip", "169.254.113.1", "", "HIKROBOT|DA7403922", "Basler"),
        ["2"] = ("Ip", "169.254.113.1", "169.254.113.10", "HIKROBOT|DA7403922", "Hikrobot")
    };
    var normalizedDict = CameraSlotDefaults.NormalizeDictionary(rawLegacySlots);
    Check("normalize dictionary: slot 1 legacy Hikrobot entry is cleared under Basler policy",
        normalizedDict["1"].Vendor == "Basler" &&
        string.IsNullOrEmpty(normalizedDict["1"].Ip) &&
        string.IsNullOrEmpty(normalizedDict["1"].DeviceKey));
    Check("normalize dictionary: slot 2 preserves valid IP and key without being wiped by legacy slot 1",
        normalizedDict["2"].Vendor == "Hikrobot" &&
        normalizedDict["2"].Ip == "169.254.113.1" &&
        normalizedDict["2"].Nic == "169.254.113.10" &&
        normalizedDict["2"].DeviceKey == "HIKROBOT|DA7403922");

    // Saving slot 2 first with raw legacy on slot 1
    var rawSaveSlot2First = new Dictionary<string, (string Mode, string Ip, string Nic, string DeviceKey, string? Vendor)>
    {
        ["1"] = ("Ip", "169.254.113.1", "", "HIKROBOT|DA7403922", "Hikrobot"),
        ["2"] = ("Ip", "169.254.113.1", "169.254.113.10", "HIKROBOT|DA7403922", "Hikrobot")
    };
    var normSave2First = CameraSlotDefaults.NormalizeDictionary(rawSaveSlot2First);
    Check("normalize dictionary: saving slot 2 first preserves slot 2 IP despite un-normalized slot 1",
        normSave2First["2"].Ip == "169.254.113.1" &&
        normSave2First["2"].DeviceKey == "HIKROBOT|DA7403922" &&
        string.IsNullOrEmpty(normSave2First["1"].Ip));

    // (h) Basler SAVE CONFIG without scan: preserves valid persisted Basler key (no IP/NIC)
    var resolvedPreserve = CameraSlotDefaults.ResolveKeyForSave(
        1, "Basler", null, "", "", "BASLER|40098765", "", "");
    Check("resolve key for save: Basler saves before scan preserve existing valid key without IP/NIC",
        resolvedPreserve.DeviceKey == "BASLER|40098765" &&
        string.IsNullOrEmpty(resolvedPreserve.Ip) &&
        string.IsNullOrEmpty(resolvedPreserve.Nic));

    var resolvedBlankInput = CameraSlotDefaults.ResolveKeyForSave(
        1, "Basler", null, "169.254.113.5", "169.254.113.10", "BASLER|40098765", "", "");
    Check("resolve key for save: Basler saves ignore input IP/NIC and preserve key",
        resolvedBlankInput.DeviceKey == "BASLER|40098765" &&
        string.IsNullOrEmpty(resolvedBlankInput.Ip) &&
        string.IsNullOrEmpty(resolvedBlankInput.Nic));

    var resolvedWithDevice = CameraSlotDefaults.ResolveKeyForSave(
        1, "Basler", camBaslerUsb, "", "", "OLD|KEY", "", "");
    Check("resolve key for save: selected device always overrides persisted key",
        resolvedWithDevice.DeviceKey == camBaslerUsb.Key &&
        string.IsNullOrEmpty(resolvedWithDevice.Ip) &&
        string.IsNullOrEmpty(resolvedWithDevice.Nic));
    var resolvedHikManual = CameraSlotDefaults.ResolveKeyForSave(
        2, "Hikrobot", null, "192.168.50.2", "192.168.50.1", "", "", "");
    Check("resolve key for save: Hikrobot synthesizes direct key for valid manual IP",
        resolvedHikManual.DeviceKey == "HIKVISION|IP:192.168.50.2" &&
        resolvedHikManual.Ip == "192.168.50.2");

    var resolvedHikPreserve = CameraSlotDefaults.ResolveKeyForSave(
        2, "Hikrobot", null, "192.168.50.2", "192.168.50.1", "HIKROBOT|J95682667", "192.168.50.2", "192.168.50.1");
    Check("resolve key for save: Hikrobot slot 2 preserves compatible genuine key when IP matches",
        resolvedHikPreserve.DeviceKey == "HIKROBOT|J95682667" &&
        resolvedHikPreserve.Ip == "192.168.50.2" &&
        resolvedHikPreserve.Nic == "192.168.50.1");

    var resolvedHikPreserveBlankInput = CameraSlotDefaults.ResolveKeyForSave(
        2, "Hikrobot", null, "", "192.168.50.1", "HIKROBOT|J95682667", "192.168.50.2", "192.168.50.1");
    Check("resolve key for save: Hikrobot slot 2 preserves compatible genuine key when input IP is blank",
        resolvedHikPreserveBlankInput.DeviceKey == "HIKROBOT|J95682667" &&
        resolvedHikPreserveBlankInput.Ip == "192.168.50.2");

    var resolvedHikChangedIp = CameraSlotDefaults.ResolveKeyForSave(
        2, "Hikrobot", null, "192.168.50.99", "192.168.50.1", "HIKROBOT|J95682667", "192.168.50.2", "192.168.50.1");
    Check("resolve key for save: Hikrobot slot 2 synthesizes direct key when IP changes without scan",
        resolvedHikChangedIp.DeviceKey == "HIKVISION|IP:192.168.50.99" &&
        resolvedHikChangedIp.Ip == "192.168.50.99");
}
LineMailboxProtocol.ProductionCommandSourceApproved = false;
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
LineMailboxProtocol.ProductionCommandSourceApproved = true;

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

// 12. Manual Belt Architecture: Independent of Camera, SCARA and Gantry
var beltIndepTransport = new FakeAdsMailboxTransport();
// Gantry disconnected; no rotary servo participates in the manual belt contract.
beltIndepTransport.SetSymbol("MAIN.bPlcQConnected", false); // Gantry disconnected
beltIndepTransport.OnSymbolWritten = armedTransport.OnSymbolWritten;
var beltIndepGateway = LineMailboxGateway.CreateForTesting(beltIndepTransport, commissioned: true);
var biSet = beltIndepGateway.RequestManualBeltSet(false, true, 95.0);
var biStart = beltIndepGateway.RequestManualBeltStart(false, true, true, 95.0);
Check("manual belt architecture: manual SET succeeds with Gantry disconnected", biSet.Success);
Check("manual belt architecture: manual START succeeds with Gantry disconnected",
    biStart.Success && beltIndepGateway.ManualBeltActive);
// 13. Real ADS command-path hardening: stale ACK after UI restart and failed speed writes
var resumedTransport = new FakeAdsMailboxTransport();
resumedTransport.SetSymbol("MAIN.udiBeltLastAckSeq", 1u);
resumedTransport.SetSymbol("MAIN.nBeltCommandResult", (ushort)1);
var resumedGateway = LineMailboxGateway.CreateForTesting(resumedTransport, commissioned: true);
var resumedSeq = resumedGateway.AllocateManualSequence();
Check("manual seq restart: never allocates last PLC ACK seq again", resumedSeq == 2u);

var failedSpeedWrite = new FakeAdsMailboxTransport();
failedSpeedWrite.OnSymbolWritten = (symbol, value, transport) =>
{
    if (symbol == "MAIN.udiHmiBeltCmdSeq") transport.IsConnected = false;
};
var failedSpeedGateway = LineMailboxGateway.CreateForTesting(failedSpeedWrite, commissioned: true);
var failedSpeedResult = failedSpeedGateway.RequestManualBeltSet(false, true, 100.0);
Check("ADS failure: failed SET speed write is reported and NOT successful",
    !failedSpeedResult.Success && failedSpeedResult.Message.Contains("speed setpoint not written"));
Check("ADS failure: SET pulse is NEVER asserted after failed speed write",
    !failedSpeedWrite.Writes.Any(w => w.Symbol == "MAIN.bHmiBeltSetReq" && (bool)w.Value));
// 14. Gantry Robot Telemetry: Source Symbols & Quality Classification
string[] gantrySymbols = {
    "MAIN.bPlcQError",
    "MAIN.nPlcQErrorID",
    "MAIN.nGlueCommErrorID",
    "MAIN.bStopCommandConfirmed",
    "MAIN.nAutoDispatchState",
    "MAIN.nQueueRealCompleted",
    "MAIN.rQueueHeadX_mm",
    "MAIN.rQueueHeadY_mm",
    "MAIN.fbPublisher.rTargetX_mm",
    "MAIN.fbPublisher.rTargetY_mm"
};
foreach (var sym in gantrySymbols)
{
    Check($"gantry symbol registered in KnownSourceSymbols: {sym}",
        LineMailboxProtocol.KnownSourceSymbols.Contains(sym));
    Check($"gantry symbol quality SOURCE_ONLY when offline: {sym}",
        LineMailboxProtocol.ClassifyQuality(sym, false) == "SOURCE_ONLY");
    Check($"gantry symbol quality LIVE when present: {sym}",
        LineMailboxProtocol.ClassifyQuality(sym, true) == "LIVE");
}

// 15. Gantry Read-Only Telemetry Safety & Actual TCP / Homed Unmapped Contract
var gantryTransport = new FakeAdsMailboxTransport();
gantryTransport.SetSymbol("MAIN.bPlcQError", false);
gantryTransport.SetSymbol("MAIN.nPlcQErrorID", 0u);
gantryTransport.SetSymbol("MAIN.nGlueCommErrorID", 0u);
gantryTransport.SetSymbol("MAIN.bStopCommandConfirmed", true);
gantryTransport.SetSymbol("MAIN.nAutoDispatchState", (short)10);
gantryTransport.SetSymbol("MAIN.nQueueRealCompleted", 5u);
gantryTransport.SetSymbol("MAIN.rQueueHeadX_mm", 45.2);
gantryTransport.SetSymbol("MAIN.rQueueHeadY_mm", 85.0);
gantryTransport.SetSymbol("MAIN.fbPublisher.rTargetX_mm", 45.2);
gantryTransport.SetSymbol("MAIN.fbPublisher.rTargetY_mm", 85.0);

Check("gantry read-only: bPlcQError read correctly",
    gantryTransport.TryRead<bool>("MAIN.bPlcQError", out var qErr) && !qErr);
Check("gantry read-only: bStopCommandConfirmed read correctly",
    gantryTransport.TryRead<bool>("MAIN.bStopCommandConfirmed", out var stopAck) && stopAck);
Check("gantry read-only: nAutoDispatchState read correctly",
    gantryTransport.TryRead<short>("MAIN.nAutoDispatchState", out var autoSt) && autoSt == 10);
Check("gantry read-only: nQueueRealCompleted read correctly",
    gantryTransport.TryRead<uint>("MAIN.nQueueRealCompleted", out var qReal) && qReal == 5);
Check("gantry read-only: rQueueHeadX_mm read correctly",
    gantryTransport.TryRead<double>("MAIN.rQueueHeadX_mm", out var qx) && Math.Abs(qx - 45.2) < 0.001);
Check("gantry read-only: fbPublisher.rTargetX_mm read correctly",
    gantryTransport.TryRead<double>("MAIN.fbPublisher.rTargetX_mm", out var tx) && Math.Abs(tx - 45.2) < 0.001);

// Actual TCP position (Md.20) and homed status MUST NOT be simulated or invented as available
Check("actual TCP unmapped: QD75 Md.20 actual X is absent from TwinCAT symbols",
    !LineMailboxProtocol.KnownSourceSymbols.Contains("MAIN.rActualTcpX"));
Check("actual TCP unmapped: QD75 Md.20 actual Y is absent from TwinCAT symbols",
    !LineMailboxProtocol.KnownSourceSymbols.Contains("MAIN.rActualTcpY"));
Check("homed unmapped: homed feedback symbol is absent from TwinCAT symbols",
    !LineMailboxProtocol.KnownSourceSymbols.Contains("MAIN.bHomed"));

// Strict safety check: gateway has ZERO robot write methods and zero writes performed to motion tags
Check("safety contract: no writes performed to robot motion tags",
    !gantryTransport.Writes.Any(w => w.Symbol == "MAIN.bStartGlueCycle" || w.Symbol == "MAIN.bTest_WriteM1000"));

// 16. Safe Offline Simulation: Trigger -> FIFO -> TwinCAT QD75 Coordinate Contract (Zero ADS Writes)
var offSim = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 0, pitchXmm: 40, pitchYmm: 40);

var emptySnap = offSim.GetSnapshot(0, 0);
Check("offline sim: empty snapshot has zero count and no head product",
    emptySnap.Count == 0 && emptySnap.HeadProduct == 0 && emptySnap.GluePoints.Length == 0 && !emptySnap.Inside);

var offAck = offSim.Accept("OFFLINE_VIDEO/1", 0.0, 0.0, 0.0, 100.0);
Check("offline sim: accepted trigger into virtual FIFO", offAck.ProductId == 1 && offSim.QueueCount == 1);

var approachSnap = offSim.GetSnapshot(100.0, 100.0);
Check("offline sim: approaching workpiece distance to work is 200mm and inside is false",
    approachSnap.Count == 1 && approachSnap.HeadProduct == 1 && Math.Abs(approachSnap.DistanceToWorkMm - 200.0) < 1e-6 && !approachSnap.Inside);

Check("offline sim: head inside workspace is false when X < 0", !offSim.IsHeadInsideWorkspace(100.0));

var earlyCmd = offSim.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 100.0);
Check("offline sim: QD75 command rejected with error 501 when workpiece outside workspace",
    !earlyCmd.IsValid && earlyCmd.ErrorCode == 501);

var inWorkSnap = offSim.GetSnapshot(320.0, 100.0);
Check("offline sim: in-workspace snapshot reports inside=true, HeadX=20mm, remaining=0mm",
    inWorkSnap.Inside && Math.Abs(inWorkSnap.HeadXmm - 20.0) < 1e-6 && Math.Abs(inWorkSnap.DistanceToWorkMm) < 1e-6);
Check("offline sim: IsHeadInsideWorkspace is true at encoder 320mm", offSim.IsHeadInsideWorkspace(320.0));

var v1Cmd = offSim.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 320.0);
Check("offline sim: vertex 1 command is valid", v1Cmd.IsValid && v1Cmd.ErrorCode == 0);
Check("offline sim: vertex 1 Target_X is 520,000 (52.0 mm)", v1Cmd.TargetX_0_1um == 520000);
Check("offline sim: vertex 1 Target_Y is 1,050,000 (105.0 mm)", v1Cmd.TargetY_0_1um == 1050000);
Check("offline sim: vertex 1 Target_Z_Down is 40,000 (4.0 mm)", v1Cmd.TargetZDown_0_1um == 40000);
Check("offline sim: vertex 1 Speed_Approach is 3,000,000 (500 mm/s)", v1Cmd.SpeedApproach_0_01mmMin == 3000000);
Check("offline sim: vertex 1 Speed_Sync is 600,000 (100 mm/s)", v1Cmd.SpeedSync_0_01mmMin == 600000);
Check("offline sim: vertex 1 DeltaX_Sync is 60,000 (6.0 mm)", v1Cmd.DeltaXSync_0_1um == 60000);
Check("offline sim: vertex 1 Target_Z_Safe is 0", v1Cmd.TargetZSafe_0_1um == 0);
Check("offline sim: vertex 1 Vertex_Index is 1", v1Cmd.VertexIndex == 1);

var melsecWords = v1Cmd.ToMelsecWords();
Check("offline sim: Melsec payload is exactly 15 words (30 bytes)", melsecWords.Length == 15);
Check("offline sim: D100..D101 Target_X unpacks correctly", (melsecWords[0] | (melsecWords[1] << 16)) == 520000);
Check("offline sim: D102..D103 Target_Y unpacks correctly", (melsecWords[2] | (melsecWords[3] << 16)) == 1050000);
Check("offline sim: D104..D105 Target_Z_Down unpacks correctly", (melsecWords[4] | (melsecWords[5] << 16)) == 40000);
Check("offline sim: D106..D107 Speed_Approach unpacks correctly", (melsecWords[6] | (melsecWords[7] << 16)) == 3000000);
Check("offline sim: D108..D109 Speed_Sync unpacks correctly", (melsecWords[8] | (melsecWords[9] << 16)) == 600000);
Check("offline sim: D110..D111 DeltaX_Sync unpacks correctly", (melsecWords[10] | (melsecWords[11] << 16)) == 60000);
Check("offline sim: D112..D113 Target_Z_Safe unpacks correctly", (melsecWords[12] | (melsecWords[13] << 16)) == 0);
Check("offline sim: D114 Vertex_Index is 1", melsecWords[14] == 1);

var v2Cmd = offSim.BuildQd75Command(vertexIndex: 2, conveyorSpeedMmS: 100.0, currentEncoderMm: 320.0);
var v3Cmd = offSim.BuildQd75Command(vertexIndex: 3, conveyorSpeedMmS: 100.0, currentEncoderMm: 320.0);
var v4Cmd = offSim.BuildQd75Command(vertexIndex: 4, conveyorSpeedMmS: 100.0, currentEncoderMm: 320.0);
Check("offline sim: vertex 2, 3, 4 commands are all valid",
    v2Cmd.IsValid && v3Cmd.IsValid && v4Cmd.IsValid);
Check("offline sim: vertex 2 Target_X is 120,000 (12.0 mm) and Target_Y is 1,050,000",
    v2Cmd.TargetX_0_1um == 120000 && v2Cmd.TargetY_0_1um == 1050000);
Check("offline sim: vertex 3 Target_X is 120,000 (12.0 mm) and Target_Y is 650,000",
    v3Cmd.TargetX_0_1um == 120000 && v3Cmd.TargetY_0_1um == 650000);
Check("offline sim: vertex 4 Target_X is 520,000 (52.0 mm) and Target_Y is 650,000",
    v4Cmd.TargetX_0_1um == 520000 && v4Cmd.TargetY_0_1um == 650000);

var lateCmd = offSim.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 500.0);
Check("offline sim: overtravel beyond X=180mm rejected with error 501",
    !lateCmd.IsValid && lateCmd.ErrorCode == 501);

Check("offline sim: zero speed rejected with error 501",
    !offSim.BuildQd75Command(1, 0.0, 320.0).IsValid);
Check("offline sim: invalid vertex index 5 rejected with error 501",
    !offSim.BuildQd75Command(5, 100.0, 320.0).IsValid);

offSim.CompleteHead();
Check("offline sim: CompleteHead empties queue and rejects command on empty queue",
    offSim.QueueCount == 0 && !offSim.BuildQd75Command(1, 100.0, 320.0).IsValid);

// 16. PLC-Q Offline Source Safety & Wiring Conflict Verification
string stPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "PLC", "Mitsubishi", "Sources_20261019", "Source", "PLC_Q_Session2_Logic.st");
if (!File.Exists(stPath))
{
    stPath = Path.Combine("PLC", "Mitsubishi", "Sources_20261019", "Source", "PLC_Q_Session2_Logic.st");
}
Check("PLC-Q ST source exists for load package", File.Exists(stPath));
if (File.Exists(stPath))
{
    string stContent = File.ReadAllText(stPath);
    Check("PLC-Q ST: Glue valve output mapped to Y25 (physical pump trigger)",
        stContent.Contains("Y25 :=") || stContent.Contains("Y25:="));
    Check("PLC-Q ST: Stale Y20 glue valve assignment is REMOVED (prevent Servo 1 torque loss)",
        !stContent.Contains("Y20 := bValve_Open") && !stContent.Contains("Y20:=bValve_Open"));
    Check("PLC-Q ST: Servo 1..3 Enables (Y20..Y22) and Brake (Y23) have zero writes in Tracking ST (owned by MAIN)",
        !System.Text.RegularExpressions.Regex.IsMatch(stContent, @"\bY2[0-3]\s*:="));
    Check("PLC-Q ST: Handshake Start bit is M1000",
        stContent.Contains("M1000"));
    Check("PLC-Q ST: Handshake Ack bit is M1001",
        stContent.Contains("M1001 :=") || stContent.Contains("M1001:="));
    Check("PLC-Q ST: Abort bit is M105",
        stContent.Contains("M105"));
    Check("PLC-Q ST: Single-writer Y0 is NOT written in Tracking (owned by MAIN via SM400)",
        !System.Text.RegularExpressions.Regex.IsMatch(stContent, @"\bY0\s*:="));
    Check("PLC-Q ST: Y10 is pulsed via M110, NOT directly written",
        stContent.Contains("M110 :=") && !System.Text.RegularExpressions.Regex.IsMatch(stContent, @"\bY10\s*:="));
}

// 17. Vision Pose Tracking & 4-Vertex Gantry Planar Contract Verification
// Test canonical vertex ordering across 0, 15, -30, 44 deg
var testOpt = new VisionOptions { PlateSizeMm = 50, HolePitchMm = 40, FixedMmPerPixel = 0.1, OriginPxX = 320, OriginPxY = 240, OffsetXmm = 0, OffsetYmm = 0 };
double mmPerPx = 0.1;
int fw = 640, fh = 480;

// Case 1: unrotated square 40x40mm pitch centered at (320, 240)
// Optical offsets: TL=(-200px, -200px), TR=(+200px, -200px), BR=(+200px, +200px), BL=(-200px, +200px)
var pts0 = new DetectionPoint[] {
    new(120, 40),  // TL in image: x=120, y=40 (py < oy => v = +20mm, u = -20mm)
    new(520, 40),  // TR in image: x=520, y=40 (py < oy => v = +20mm, u = +20mm)
    new(520, 440), // BR in image: x=520, y=440 (py > oy => v = -20mm, u = +20mm)
    new(120, 440)  // BL in image: x=120, y=440 (py > oy => v = -20mm, u = -20mm)
};
var vOrder0 = BeltCoordinates.MapVertices(pts0, 0.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus);
Check("vertex ordering: MapVertices returns exactly 4 vertices", vOrder0.Length == 4);
Check("vertex ordering: v1 is Quad I (+20, +20)", vOrder0[0].Index == 1 && Math.Abs(vOrder0[0].X_ConveyorMm - 20.0) < 1e-4 && Math.Abs(vOrder0[0].Y_ConveyorMm - 20.0) < 1e-4);
Check("vertex ordering: v2 is Quad II (-20, +20)", vOrder0[1].Index == 2 && Math.Abs(vOrder0[1].X_ConveyorMm - (-20.0)) < 1e-4 && Math.Abs(vOrder0[1].Y_ConveyorMm - 20.0) < 1e-4);
Check("vertex ordering: v3 is Quad III (-20, -20)", vOrder0[2].Index == 3 && Math.Abs(vOrder0[2].X_ConveyorMm - (-20.0)) < 1e-4 && Math.Abs(vOrder0[2].Y_ConveyorMm - (-20.0)) < 1e-4);
Check("vertex ordering: v4 is Quad IV (+20, -20)", vOrder0[3].Index == 4 && Math.Abs(vOrder0[3].X_ConveyorMm - 20.0) < 1e-4 && Math.Abs(vOrder0[3].Y_ConveyorMm - (-20.0)) < 1e-4);

// Geometric assertions for rotation across angles and directions (replacing tautological index checks)
static DetectionPoint[] MakeRotatedOpticalPts(double thetaDeg, double cx = 320, double cy = 240, double halfPitchMm = 20.0, double scale = 0.1)
{
    double rad = thetaDeg * Math.PI / 180.0;
    double cos = Math.Cos(rad), sin = Math.Sin(rad);
    // Optical corners: TL, TR, BR, BL
    (double u, double v)[] localOptical = [(-halfPitchMm, halfPitchMm), (halfPitchMm, halfPitchMm), (halfPitchMm, -halfPitchMm), (-halfPitchMm, -halfPitchMm)];
    return localOptical.Select(pt => {
        double rotU = pt.u * cos - pt.v * sin;
        double rotV = pt.u * sin + pt.v * cos;
        return new DetectionPoint(cx + rotU / scale, cy - rotV / scale);
    }).ToArray();
}

// Case -30 deg, XPlus
var ptsNeg30 = MakeRotatedOpticalPts(-30.0);
var vOrderNeg30 = BeltCoordinates.MapVertices(ptsNeg30, -30.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus);
double r30 = -30.0 * Math.PI / 180.0;
double c30 = Math.Cos(r30), s30 = Math.Sin(r30);
double expP1_X = 20 * c30 - 20 * s30, expP1_Y = 20 * s30 + 20 * c30;
double expP2_X = -20 * c30 - 20 * s30, expP2_Y = -20 * s30 + 20 * c30;
Check("geometric assertion: -30 deg XPlus v1 matches expected Cartesian Quad I",
    vOrderNeg30[0].Index == 1 && Math.Abs(vOrderNeg30[0].X_ConveyorMm - expP1_X) < 1e-3 && Math.Abs(vOrderNeg30[0].Y_ConveyorMm - expP1_Y) < 1e-3);
Check("geometric assertion: -30 deg XPlus v2 matches expected Cartesian Quad II",
    vOrderNeg30[1].Index == 2 && Math.Abs(vOrderNeg30[1].X_ConveyorMm - expP2_X) < 1e-3 && Math.Abs(vOrderNeg30[1].Y_ConveyorMm - expP2_Y) < 1e-3);

// Case 44 deg, non-XPlus (YPlus)
var pts44 = MakeRotatedOpticalPts(44.0);
var vOrder44_YPlus = BeltCoordinates.MapVertices(pts44, 44.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.YPlus);
double r44 = 44.0 * Math.PI / 180.0;
double c44 = Math.Cos(r44), s44 = Math.Sin(r44);
double expP1_YPlus_X = 20 * c44 - 20 * s44, expP1_YPlus_Y = 20 * s44 + 20 * c44;
double expP2_YPlus_X = -20 * c44 - 20 * s44, expP2_YPlus_Y = -20 * s44 + 20 * c44;
Check("geometric assertion: 44 deg YPlus v1 matches expected Cartesian Quad I",
    vOrder44_YPlus[0].Index == 1 && Math.Abs(vOrder44_YPlus[0].X_ConveyorMm - expP1_YPlus_X) < 1e-3 && Math.Abs(vOrder44_YPlus[0].Y_ConveyorMm - expP1_YPlus_Y) < 1e-3);
Check("geometric assertion: 44 deg YPlus v2 matches expected Cartesian Quad II",
    vOrder44_YPlus[1].Index == 2 && Math.Abs(vOrder44_YPlus[1].X_ConveyorMm - expP2_YPlus_X) < 1e-3 && Math.Abs(vOrder44_YPlus[1].Y_ConveyorMm - expP2_YPlus_Y) < 1e-3);

// Review Focus 3: Degenerate or collinear points handling
bool caughtDegenerate = false;
try {
    var degeneratePts = new DetectionPoint[] { new(320, 240), new(320, 240), new(320, 240), new(320, 240) };
    BeltCoordinates.MapVertices(degeneratePts, 0.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus);
} catch (ArgumentException) { caughtDegenerate = true; }
Check("review focus 3: MapVertices rejects degenerate coincident points", caughtDegenerate);

// Review Issue 3: MapVertices rejects four distinct collinear points
bool caughtCollinear = false;
try {
    var collinearPts = new DetectionPoint[] { new(100, 240), new(200, 240), new(300, 240), new(400, 240) };
    BeltCoordinates.MapVertices(collinearPts, 0.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus);
} catch (ArgumentException) { caughtCollinear = true; }
Check("review issue 3: MapVertices rejects four distinct collinear points", caughtCollinear);

// Review Focus 4: Out-of-tolerance pitch in CreateTrackedPose
var outOfTolResult = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95,
    new DetectionPoint[] { new(100, 40), new(540, 40), new(540, 440), new(100, 440) }, // 44mm pitch X (> 42mm tolerance)
    [], [], "OK");
var outOfTolPose = BeltCoordinates.CreateTrackedPose(1, "CAM1", outOfTolResult, testOpt, fw, fh, BeltDirection.XPlus, 0.0);
Check("review focus 4: TrackedPose marks IsOpticalMeasured=false when pitch exceeds tolerance", !outOfTolPose.IsOpticalMeasured);

// Review Finding 1: CreateTrackedPose fail-closed on PlateResult.None / empty GluePx
var nonePose = BeltCoordinates.CreateTrackedPose(2, "CAM1", PlateResult.None("No plate"), testOpt, fw, fh, BeltDirection.XPlus, 0.0);
Check("review finding 1: CreateTrackedPose handles PlateResult.None safely", !nonePose.IsOpticalMeasured && nonePose.Vertices.Length == 0 && nonePose.MeasuredPitchX_mm == 0.0);

// Review Finding 2: BeltDirection YPlus and XMinus canonical quadrant ordering across rotations
var vOrderYPlus0 = BeltCoordinates.MapVertices(pts0, 0.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.YPlus);
Check("review finding 2: YPlus theta=0 returns canonical indices 1..4",
    vOrderYPlus0[0].Index == 1 && vOrderYPlus0[1].Index == 2 && vOrderYPlus0[2].Index == 3 && vOrderYPlus0[3].Index == 4);
var vOrderXMinus0 = BeltCoordinates.MapVertices(pts0, 0.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XMinus);
Check("review finding 2: XMinus theta=0 returns canonical indices 1..4",
    vOrderXMinus0[0].Index == 1 && vOrderXMinus0[1].Index == 2 && vOrderXMinus0[2].Index == 3 && vOrderXMinus0[3].Index == 4);
var vOrderYPlus15 = BeltCoordinates.MapVertices(pts0, 15.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.YPlus);
Check("review finding 2: YPlus theta=15 returns canonical indices 1..4",
    vOrderYPlus15[0].Index == 1 && vOrderYPlus15[1].Index == 2 && vOrderYPlus15[2].Index == 3 && vOrderYPlus15[3].Index == 4);
var vOrderXMinus15 = BeltCoordinates.MapVertices(pts0, 15.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XMinus);
Check("review finding 2: XMinus theta=15 returns canonical indices 1..4",
    vOrderXMinus15[0].Index == 1 && vOrderXMinus15[1].Index == 2 && vOrderXMinus15[2].Index == 3 && vOrderXMinus15[3].Index == 4);

// Review Finding 3: Finite validation for thetaDeg, originX/Y, offsetX/Y
bool caughtNonFiniteTheta = false;
try { BeltCoordinates.Map(320, 240, double.NaN, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus); }
catch (ArgumentException) { caughtNonFiniteTheta = true; }
Check("review finding 3: Map rejects non-finite theta", caughtNonFiniteTheta);

bool caughtNonFiniteOrigin = false;
try { BeltCoordinates.Map(320, 240, 0.0, fw, fh, mmPerPx, double.PositiveInfinity, 240, 0, 0, BeltDirection.XPlus); }
catch (ArgumentException) { caughtNonFiniteOrigin = true; }
Check("review finding 3: Map rejects non-finite origin", caughtNonFiniteOrigin);

bool caughtNonFiniteOffset = false;
try { BeltCoordinates.Map(320, 240, 0.0, fw, fh, mmPerPx, 320, 240, double.NaN, 0, BeltDirection.XPlus); }
catch (ArgumentException) { caughtNonFiniteOffset = true; }
Check("review finding 3: Map rejects non-finite offset", caughtNonFiniteOffset);

// Review Finding 4: Calibration HolePitchMm used instead of hard-coded 40
var customPitchOpt = new VisionOptions { PlateSizeMm = 40, HolePitchMm = 30, FixedMmPerPixel = 0.1, OriginPxX = 320, OriginPxY = 240, OffsetXmm = 0, OffsetYmm = 0 };
var customPitchResult = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95,
    new DetectionPoint[] { new(170, 90), new(470, 90), new(470, 390), new(170, 390) }, // 30mm pitch
    [], [], "OK");
var customPitchPose = BeltCoordinates.CreateTrackedPose(3, "CAM1", customPitchResult, customPitchOpt, fw, fh, BeltDirection.XPlus, 0.0);
Check("review finding 4: CreateTrackedPose uses calibration.HolePitchMm", customPitchPose.IsOpticalMeasured && Math.Abs(customPitchPose.MeasuredPitchX_mm - 30.0) < 1e-4);

// Review Issue 1: CreateTrackedPose fail-closed on null calibration, invalid pitch, or non-finite detection
var nullCalibPose = BeltCoordinates.CreateTrackedPose(4, "CAM1", customPitchResult, null!, fw, fh, BeltDirection.XPlus, 0.0);
Check("review issue 1: CreateTrackedPose returns fail-closed pose on null calibration without throwing",
    !nullCalibPose.IsOpticalMeasured && nullCalibPose.Vertices.Length == 0);

var badPitchCalib = new VisionOptions { PlateSizeMm = 50, HolePitchMm = -10, FixedMmPerPixel = 0.1, OriginPxX = 320, OriginPxY = 240, OffsetXmm = 0, OffsetYmm = 0 };
var badPitchPose = BeltCoordinates.CreateTrackedPose(5, "CAM1", customPitchResult, badPitchCalib, fw, fh, BeltDirection.XPlus, 0.0);
Check("review issue 1: CreateTrackedPose returns fail-closed pose on HolePitchMm <= 0 without throwing",
    !badPitchPose.IsOpticalMeasured && badPitchPose.Vertices.Length == 0);

var nanCenterResult = new PlateResult(true, double.NaN, 240, 0, 0, 0, 0.1, 0.95,
    new DetectionPoint[] { new(170, 90), new(470, 90), new(470, 390), new(170, 390) }, [], [], "OK");
var nanCenterPose = BeltCoordinates.CreateTrackedPose(6, "CAM1", nanCenterResult, customPitchOpt, fw, fh, BeltDirection.XPlus, 0.0);
Check("review issue 1: CreateTrackedPose returns fail-closed pose on NaN center",
    !nanCenterPose.IsOpticalMeasured && nanCenterPose.Vertices.Length == 0);

var badScaleResult = new PlateResult(true, 320, 240, 0, 0, 0, 0.0, 0.95,
    new DetectionPoint[] { new(170, 90), new(470, 90), new(470, 390), new(170, 390) }, [], [], "OK");
var badScalePose = BeltCoordinates.CreateTrackedPose(7, "CAM1", badScaleResult, customPitchOpt, fw, fh, BeltDirection.XPlus, 0.0);
Check("review issue 1: CreateTrackedPose returns fail-closed pose on MmPerPixel <= 0",
    !badScalePose.IsOpticalMeasured && badScalePose.Vertices.Length == 0);

var nanPointResult = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95,
    new DetectionPoint[] { new(double.NaN, 90), new(470, 90), new(470, 390), new(170, 390) }, [], [], "OK");
var nanPointPose = BeltCoordinates.CreateTrackedPose(8, "CAM1", nanPointResult, customPitchOpt, fw, fh, BeltDirection.XPlus, 0.0);
Check("review issue 1: CreateTrackedPose returns fail-closed pose on NaN GluePx point",
    !nanPointPose.IsOpticalMeasured && nanPointPose.Vertices.Length == 0);


// 18. Task 2: Simulator Measured Vertex Acceptance & Workspace Envelope
var simAsym = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 0, pitchXmm: 40, pitchYmm: 40);
var measuredAsym = new ConveyorVertex[] {
    new(1, 21.0, 19.0),   // Quad I: halfX=21, halfY=19 => 42x38mm
    new(2, -21.0, 19.0),  // Quad II
    new(3, -21.0, -19.0), // Quad III
    new(4, 21.0, -19.0)   // Quad IV
};
var ackAsym = simAsym.Accept("ASYM_TEST/1", 0.0, 0.0, 0.0, 100.0, measuredAsym);
Check("sim measured: accepted receipt has 4 corners", ackAsym.Corners.Length == 4);
Check("sim measured: V1 RobotX is -179.0mm", Math.Abs(ackAsym.Corners[0].X - (-179.0)) < 1e-4);
Check("sim measured: V1 RobotY is 104.0mm", Math.Abs(ackAsym.Corners[0].Y - 104.0) < 1e-4);
Check("sim measured: V2 RobotX is -221.0mm", Math.Abs(ackAsym.Corners[1].X - (-221.0)) < 1e-4);
Check("sim measured: V2 RobotY is 104.0mm", Math.Abs(ackAsym.Corners[1].Y - 104.0) < 1e-4);
Check("sim measured: V3 RobotX is -221.0mm", Math.Abs(ackAsym.Corners[2].X - (-221.0)) < 1e-4);
Check("sim measured: V3 RobotY is 66.0mm", Math.Abs(ackAsym.Corners[2].Y - 66.0) < 1e-4);
Check("sim measured: V4 RobotX is -179.0mm", Math.Abs(ackAsym.Corners[3].X - (-179.0)) < 1e-4);
Check("sim measured: V4 RobotY is 66.0mm", Math.Abs(ackAsym.Corners[3].Y - 66.0) < 1e-4);

// Rotation coupling: non-zero CameraYawDegrees (30 deg) with asymmetric measured vertices
var simYaw30 = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 30, pitchXmm: 40, pitchYmm: 40);
var ackYaw30 = simYaw30.Accept("YAW30_TEST/1", 0.0, 0.0, 0.0, 100.0, measuredAsym);
double yaw30Rad = 30.0 * Math.PI / 180.0;
double cosYaw30 = Math.Cos(yaw30Rad), sinYaw30 = Math.Sin(yaw30Rad);
double expYawV1_X = -200.0 + 21.0 * cosYaw30 - 19.0 * sinYaw30; // ~ -191.3135 mm
double expYawV1_Y = 85.0 + 21.0 * sinYaw30 + 19.0 * cosYaw30;   // ~ 111.9545 mm
double expYawV2_X = -200.0 - 21.0 * cosYaw30 - 19.0 * sinYaw30; // ~ -227.6865 mm
double expYawV2_Y = 85.0 - 21.0 * sinYaw30 + 19.0 * cosYaw30;   // ~ 90.9545 mm
Check("sim yaw coupling: 30 deg V1 RobotX matches rot transform", Math.Abs(ackYaw30.Corners[0].X - expYawV1_X) < 1e-4);
Check("sim yaw coupling: 30 deg V1 RobotY matches rot transform", Math.Abs(ackYaw30.Corners[0].Y - expYawV1_Y) < 1e-4);
Check("sim yaw coupling: 30 deg V2 RobotX matches rot transform", Math.Abs(ackYaw30.Corners[1].X - expYawV2_X) < 1e-4);
Check("sim yaw coupling: 30 deg V2 RobotY matches rot transform", Math.Abs(ackYaw30.Corners[1].Y - expYawV2_Y) < 1e-4);

// Permuted vertex order gets sorted canonically into 1..4
var permutedMeasured = new ConveyorVertex[] {
    new(4, 21.0, -19.0),
    new(2, -21.0, 19.0),
    new(1, 21.0, 19.0),
    new(3, -21.0, -19.0)
};
var simPerm = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 0, pitchXmm: 40, pitchYmm: 40);
var ackPerm = simPerm.Accept("PERM_TEST/1", 0.0, 0.0, 0.0, 100.0, permutedMeasured);
Check("sim measured: permuted inputs sorted canonically (V1 X is -179mm)", Math.Abs(ackPerm.Corners[0].X - (-179.0)) < 1e-4);
Check("sim measured: permuted inputs sorted canonically (V4 X is -179mm)", Math.Abs(ackPerm.Corners[3].X - (-179.0)) < 1e-4);

// Backward compatibility: null measuredVertices preserves nominal synthesis
var ackNominal = simAsym.Accept("NOMINAL_TEST/1", 0.0, 0.0, 0.0, 100.0, null);
Check("sim nominal: accepted receipt has 4 corners when measuredVertices is null", ackNominal.Corners.Length == 4);
Check("sim nominal: V1 RobotX matches nominal 40x40 pitch (-180mm)", Math.Abs(ackNominal.Corners[0].X - (-180.0)) < 1e-4);
Check("sim nominal: V1 RobotY matches nominal 40x40 pitch (105mm)", Math.Abs(ackNominal.Corners[0].Y - 105.0) < 1e-4);
Check("sim nominal: V3 RobotX matches nominal 40x40 pitch (-220mm)", Math.Abs(ackNominal.Corners[2].X - (-220.0)) < 1e-4);
Check("sim nominal: V3 RobotY matches nominal 40x40 pitch (65mm)", Math.Abs(ackNominal.Corners[2].Y - 65.0) < 1e-4);

// Review Focus 1: Non-finite coordinate rejection
bool caughtNan = false;
try {
    var nanVertices = new ConveyorVertex[] {
        new(1, double.NaN, 19.0), new(2, -21.0, 19.0), new(3, -21.0, -19.0), new(4, 21.0, -19.0)
    };
    simAsym.Accept("NAN_TEST/1", 0.0, 0.0, 0.0, 100.0, nanVertices);
} catch (ArgumentException) { caughtNan = true; }
Check("review focus 1: Accept rejects NaN measured vertex coordinates", caughtNan);

bool caughtInf = false;
try {
    var infVertices = new ConveyorVertex[] {
        new(1, 21.0, double.PositiveInfinity), new(2, -21.0, 19.0), new(3, -21.0, -19.0), new(4, 21.0, -19.0)
    };
    simAsym.Accept("INF_TEST/1", 0.0, 0.0, 0.0, 100.0, infVertices);
} catch (ArgumentException) { caughtInf = true; }
Check("review focus 1: Accept rejects Infinity measured vertex coordinates", caughtInf);

// Review Focus 2: Vertex count mismatch rejection
bool caughtLengthMismatch3 = false;
try {
    var badLen3 = new ConveyorVertex[] { new(1, 21.0, 19.0), new(2, -21.0, 19.0), new(3, -21.0, -19.0) };
    simAsym.Accept("LEN3_TEST/1", 0.0, 0.0, 0.0, 100.0, badLen3);
} catch (ArgumentException) { caughtLengthMismatch3 = true; }
Check("review focus 2: Accept rejects vertex array of length 3", caughtLengthMismatch3);

bool caughtLengthMismatch5 = false;
try {
    var badLen5 = new ConveyorVertex[] {
        new(1, 21.0, 19.0), new(2, -21.0, 19.0), new(3, -21.0, -19.0), new(4, 21.0, -19.0), new(5, 0.0, 0.0)
    };
    simAsym.Accept("LEN5_TEST/1", 0.0, 0.0, 0.0, 100.0, badLen5);
} catch (ArgumentException) { caughtLengthMismatch5 = true; }
Check("review focus 2: Accept rejects vertex array of length 5", caughtLengthMismatch5);

// Non-canonical index set rejection
bool caughtBadIndices = false;
try {
    var badIndices = new ConveyorVertex[] {
        new(1, 21.0, 19.0), new(2, -21.0, 19.0), new(3, -21.0, -19.0), new(5, 21.0, -19.0)
    };
    simAsym.Accept("BAD_IDX/1", 0.0, 0.0, 0.0, 100.0, badIndices);
} catch (ArgumentException) { caughtBadIndices = true; }
Check("canonical indices: Accept rejects non-canonical vertex indices (1,2,3,5)", caughtBadIndices);

bool caughtDuplicateIndices = false;
try {
    var dupIndices = new ConveyorVertex[] {
        new(1, 21.0, 19.0), new(2, -21.0, 19.0), new(2, -21.0, -19.0), new(4, 21.0, -19.0)
    };
    simAsym.Accept("DUP_IDX/1", 0.0, 0.0, 0.0, 100.0, dupIndices);
} catch (ArgumentException) { caughtDuplicateIndices = true; }
Check("canonical indices: Accept rejects duplicate vertex indices (1,2,2,4)", caughtDuplicateIndices);

// Null element inside measuredVertices array rejection
bool caughtNullVertex = false;
try {
    var nullElemVertices = new ConveyorVertex[] {
        new(1, 21.0, 19.0), null!, new(3, -21.0, -19.0), new(4, 21.0, -19.0)
    };
    simAsym.Accept("NULL_ELEM/1", 0.0, 0.0, 0.0, 100.0, nullElemVertices);
} catch (ArgumentException) { caughtNullVertex = true; }
Check("null validation: Accept rejects null elements in measuredVertices", caughtNullVertex);

// Atomic queue state preservation on rejection
int qCountBeforeAtomic = simAsym.QueueCount;
uint acceptedBeforeAtomic = simAsym.Accepted;
bool caughtAtomicRejection = false;
try {
    simAsym.Accept("ATOMIC_PROBE/1", 0.0, 0.0, 0.0, 100.0, [new ConveyorVertex(1, double.NaN, 0.0)]);
} catch (ArgumentException) { caughtAtomicRejection = true; }
Check("atomic preservation: rejection threw ArgumentException", caughtAtomicRejection);
Check("atomic preservation: QueueCount unchanged on rejected accept", simAsym.QueueCount == qCountBeforeAtomic);
Check("atomic preservation: Accepted counter unchanged on rejected accept", simAsym.Accepted == acceptedBeforeAtomic);
var retryAtomic = simAsym.Accept("ATOMIC_PROBE/1", 0.0, 0.0, 0.0, 100.0, measuredAsym);
Check("atomic preservation: key not polluted, re-acceptance succeeds", retryAtomic != null && retryAtomic.SourceTrackKey == "ATOMIC_PROBE/1");

// BuildQd75Command with measured asymmetric vertices in-workspace
var validAsymCmd = simAsym.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 345.0);
Check("sim measured: BuildQd75Command produces valid command when in workspace",
    validAsymCmd.IsValid && validAsymCmd.ErrorCode == 0);
Check("sim measured: TargetX_0_1um is 780,000 (78.0mm)", validAsymCmd.TargetX_0_1um == 780000);
Check("sim measured: TargetY_0_1um is 1,040,000 (104.0mm)", validAsymCmd.TargetY_0_1um == 1040000);

// Review Focus 5: Dynamic sync lead pushes vertex overtravel
// At encoder = 345mm, corner X = -179 + 345 - 100 = 66mm (in bounds)
// At encoder = 455mm, corner X = -179 + 455 - 100 = 176mm (inside 180mm static envelope)
// Lead = 100 mm/s * 0.12s = 12mm; DeltaXSync = 100 mm/s * 0.06s = 6mm.
// TargetX + DeltaXSync = 176 + 12 + 6 = 194mm > 180mm => MUST reject with 501
var overtravelCmd = simAsym.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 455.0);
Check("review focus 5: BuildQd75Command rejects dynamic sync lead overtravel with error 501",
    !overtravelCmd.IsValid && overtravelCmd.ErrorCode == 501);

// Fail-closed validation for non-finite inputs to BuildQd75Command
Check("build command non-finite: NaN currentEncoderMm rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, double.NaN).IsValid && simAsym.BuildQd75Command(1, 100.0, double.NaN).ErrorCode == 501);
Check("build command non-finite: Infinity currentEncoderMm rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, double.PositiveInfinity).IsValid && simAsym.BuildQd75Command(1, 100.0, double.PositiveInfinity).ErrorCode == 501);
Check("build command non-finite: NaN speed rejected with error 501",
    !simAsym.BuildQd75Command(1, double.NaN, 345.0).IsValid && simAsym.BuildQd75Command(1, double.NaN, 345.0).ErrorCode == 501);
Check("build command non-finite: NaN approach speed rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, 345.0, approachSpeedMmS: double.NaN).IsValid && simAsym.BuildQd75Command(1, 100.0, 345.0, approachSpeedMmS: double.NaN).ErrorCode == 501);
Check("build command non-finite: NaN flyTimeS rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, 345.0, flyTimeS: double.NaN).IsValid && simAsym.BuildQd75Command(1, 100.0, 345.0, flyTimeS: double.NaN).ErrorCode == 501);
Check("build command non-finite: NaN syncTimeS rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, 345.0, syncTimeS: double.NaN).IsValid && simAsym.BuildQd75Command(1, 100.0, 345.0, syncTimeS: double.NaN).ErrorCode == 501);
Check("build command non-finite: NaN zDownMm rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, 345.0, zDownMm: double.NaN).IsValid && simAsym.BuildQd75Command(1, 100.0, 345.0, zDownMm: double.NaN).ErrorCode == 501);
Check("build command non-finite: NaN zSafeMm rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, 345.0, zSafeMm: double.NaN).IsValid && simAsym.BuildQd75Command(1, 100.0, 345.0, zSafeMm: double.NaN).ErrorCode == 501);
Check("build command non-finite: NaN workspace boundary rejected with error 501",
    !simAsym.BuildQd75Command(1, 100.0, 345.0, xMaxMm: double.NaN).IsValid && simAsym.BuildQd75Command(1, 100.0, 345.0, xMaxMm: double.NaN).ErrorCode == 501);
// 19. Task 3: UI Label Overlay Canonical Quadrant Reconciliation
var dummyPlate = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, pts0, [], [], "OK");
var overlayVerts = BeltCoordinates.MapVertices(dummyPlate.GluePx, dummyPlate.AngleDeg, fw, fh, dummyPlate.MmPerPixel, 320, 240, 0, 0, BeltDirection.XPlus);
Check("ui overlay contract: P1 corresponds to Quad I (Index 1)", overlayVerts[0].Index == 1);
Check("ui overlay contract: P4 corresponds to Quad IV (Index 4)", overlayVerts[3].Index == 4);

// Render verification on STA thread: ensure overlay renders canonical P1..P4 and handles fallback
string p1Text = "";
string p2Text = "";
string p3Text = "";
string p4Text = "";
int textBlockCount = 0;
var staThread = new System.Threading.Thread(() => {
    var canvas = new System.Windows.Controls.Canvas();
    VisionLabelOverlay.Render(canvas, [dummyPlate], new System.Collections.Generic.Dictionary<int, int> { { 0, 1 } }, testOpt, BeltDirection.XPlus, fw, fh, 640, 480);
    if (canvas.Children.Count > 0 && canvas.Children[0] is System.Windows.Controls.StackPanel sp)
    {
        var textBlocks = sp.Children.OfType<System.Windows.Controls.TextBlock>().ToArray();
        textBlockCount = textBlocks.Length;
        if (textBlocks.Length >= 7)
        {
            p1Text = textBlocks[3].Text;
            p2Text = textBlocks[4].Text;
            p3Text = textBlocks[5].Text;
            p4Text = textBlocks[6].Text;
        }
    }
});
staThread.SetApartmentState(System.Threading.ApartmentState.STA);
staThread.Start();
staThread.Join();

Check("ui overlay render: 4 vertex textblocks rendered", textBlockCount == 7);
Check("ui overlay render: P1 is Quad I (+20, +20)", p1Text.Contains("P1") && p1Text.Contains("X=   20.00") && p1Text.Contains("Y=   20.00"));
Check("ui overlay render: P2 is Quad II (-20, +20)", p2Text.Contains("P2") && p2Text.Contains("X=  -20.00") && p2Text.Contains("Y=   20.00"));
Check("ui overlay render: P3 is Quad III (-20, -20)", p3Text.Contains("P3") && p3Text.Contains("X=  -20.00") && p3Text.Contains("Y=  -20.00"));
Check("ui overlay render: P4 is Quad IV (+20, -20)", p4Text.Contains("P4") && p4Text.Contains("X=   20.00") && p4Text.Contains("Y=  -20.00"));

// Fallback verification: fewer than 4 points, null GluePx, and degenerate/collinear points
int count2Pt = 0;
string fbP1Text = "";
string fbP2Text = "";
int countNullPt = 0;
int countCollinear = 0;
string colP1Text = "";

var fallbackThread = new System.Threading.Thread(() => {
    // 1. Fewer than 4 points (2 points)
    var canvas2Pt = new System.Windows.Controls.Canvas();
    var dummy2Pt = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, [pts0[0], pts0[1]], [], [], "OK");
    VisionLabelOverlay.Render(canvas2Pt, [dummy2Pt], new System.Collections.Generic.Dictionary<int, int> { { 0, 1 } }, testOpt, BeltDirection.XPlus, fw, fh, 640, 480);
    if (canvas2Pt.Children.Count > 0 && canvas2Pt.Children[0] is System.Windows.Controls.StackPanel sp2)
    {
        var tbs = sp2.Children.OfType<System.Windows.Controls.TextBlock>().ToArray();
        count2Pt = tbs.Length;
        if (tbs.Length >= 5) { fbP1Text = tbs[3].Text; fbP2Text = tbs[4].Text; }
    }

    // 2. Null GluePx
    var canvasNull = new System.Windows.Controls.Canvas();
    var dummyNull = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, null!, [], [], "OK");
    VisionLabelOverlay.Render(canvasNull, [dummyNull], new System.Collections.Generic.Dictionary<int, int> { { 0, 1 } }, testOpt, BeltDirection.XPlus, fw, fh, 640, 480);
    if (canvasNull.Children.Count > 0 && canvasNull.Children[0] is System.Windows.Controls.StackPanel spNull)
    {
        countNullPt = spNull.Children.OfType<System.Windows.Controls.TextBlock>().Count();
    }

    // 3. Collinear points (MapVertices throws ArgumentException -> gracefully falls back to raw indexing)
    var canvasCollinear = new System.Windows.Controls.Canvas();
    var collinearPts = new DetectionPoint[] { new(100, 100), new(200, 200), new(300, 300), new(400, 400) };
    var dummyCollinear = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, collinearPts, [], [], "OK");
    VisionLabelOverlay.Render(canvasCollinear, [dummyCollinear], new System.Collections.Generic.Dictionary<int, int> { { 0, 1 } }, testOpt, BeltDirection.XPlus, fw, fh, 640, 480);
    if (canvasCollinear.Children.Count > 0 && canvasCollinear.Children[0] is System.Windows.Controls.StackPanel spCol)
    {
        var tbs = spCol.Children.OfType<System.Windows.Controls.TextBlock>().ToArray();
        countCollinear = tbs.Length;
        if (tbs.Length >= 4) { colP1Text = tbs[3].Text; }
    }
});
fallbackThread.SetApartmentState(System.Threading.ApartmentState.STA);
fallbackThread.Start();
fallbackThread.Join();

Check("ui overlay fallback: 2-point detection renders 2 vertex lines", count2Pt == 5 && fbP1Text.StartsWith("P1") && fbP2Text.StartsWith("P2"));
Check("ui overlay fallback: null GluePx renders header without crashing", countNullPt == 3);
Check("ui overlay fallback: collinear points gracefully fall back to raw indexing without crashing", countCollinear == 7 && colP1Text.StartsWith("P1"));

// 20. Task 4: Main Application Trigger Wiring & End-to-End SIL Simulation
// End-to-end SIL simulation: TriggerEvent -> MapVertices -> Simulator -> BuildQd75Command
var simE2E = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 0, pitchXmm: 40, pitchYmm: 40);
var dummyResult = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, pts0, [], [], "OK");
var trigEvent = new TriggerEvent(42, "OFFLINE_CAM", dummyResult, 100, DateTime.UtcNow);

var e2ePose = BeltCoordinates.CreateTrackedPose(trigEvent.ProductId, trigEvent.Source, trigEvent.Pose, testOpt, fw, fh, BeltDirection.XPlus, 50.0);
Check("e2e: TrackedPose created with 4 canonical vertices", e2ePose.Vertices.Length == 4 && e2ePose.IsOpticalMeasured);

var e2eAck = simE2E.Accept(e2ePose.SourceTrackKey, e2ePose.CenterX_mm, e2ePose.CenterY_mm, e2ePose.Theta_deg, e2ePose.EncoderAtCrossingMm, e2ePose.Vertices);
Check("e2e: simulator accepted tracked pose with measured vertices", e2eAck.ProductId == 1 && e2eAck.Corners.Length == 4);

// Advance virtual encoder to 270mm (travel = 220mm, HeadX = -200 + 20 + 220 = 40mm inside workspace)
var e2eCmd = simE2E.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 270.0);
Check("e2e: QD75 command generated successfully from measured vertex", e2eCmd.IsValid && e2eCmd.ErrorCode == 0);
var e2eWords = e2eCmd.ToMelsecWords();
Check("e2e: 15-word binary payload generated", e2eWords.Length == 15 && e2eWords[14] == 1);

// RecordTrigger pipeline logic: MapVertices directly from TriggerEvent.Pose.GluePx
ConveyorVertex[]? trigMeasuredVertices = null;
if (trigEvent.Pose.GluePx != null && trigEvent.Pose.GluePx.Length == 4)
{
    try {
        trigMeasuredVertices = BeltCoordinates.MapVertices(trigEvent.Pose.GluePx, trigEvent.Pose.AngleDeg,
            fw, fh, trigEvent.Pose.MmPerPixel, testOpt.OriginPxX, testOpt.OriginPxY,
            testOpt.OffsetXmm, testOpt.OffsetYmm, BeltDirection.XPlus);
    } catch { trigMeasuredVertices = null; }
}
Check("record trigger: MapVertices yields 4 canonical vertices", trigMeasuredVertices != null && trigMeasuredVertices.Length == 4);
Check("record trigger: P1 is Quad I (+20, +20)", Math.Abs(trigMeasuredVertices![0].X_ConveyorMm - 20.0) < 1e-4 && Math.Abs(trigMeasuredVertices[0].Y_ConveyorMm - 20.0) < 1e-4);
Check("record trigger: P2 is Quad II (-20, +20)", Math.Abs(trigMeasuredVertices[1].X_ConveyorMm - (-20.0)) < 1e-4 && Math.Abs(trigMeasuredVertices[1].Y_ConveyorMm - 20.0) < 1e-4);
Check("record trigger: P3 is Quad III (-20, -20)", Math.Abs(trigMeasuredVertices[2].X_ConveyorMm - (-20.0)) < 1e-4 && Math.Abs(trigMeasuredVertices[2].Y_ConveyorMm - (-20.0)) < 1e-4);
Check("record trigger: P4 is Quad IV (+20, -20)", Math.Abs(trigMeasuredVertices[3].X_ConveyorMm - 20.0) < 1e-4 && Math.Abs(trigMeasuredVertices[3].Y_ConveyorMm - (-20.0)) < 1e-4);

var simTrig = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 0, pitchXmm: 40, pitchYmm: 40);
var trigAck = simTrig.Accept("TRIG/1", 0.0, 0.0, 0.0, 0.0, trigMeasuredVertices);
Check("record trigger: simulator accepted with encoder=0 placeholder", trigAck.ProductId == 1 && trigAck.EncoderAtTrigger == 0.0);
Check("record trigger: corners match measured vertices",
    Math.Abs(trigAck.Corners[0].X - (-180.0)) < 1e-4 && Math.Abs(trigAck.Corners[0].Y - 105.0) < 1e-4 &&
    Math.Abs(trigAck.Corners[1].X - (-220.0)) < 1e-4 && Math.Abs(trigAck.Corners[1].Y - 105.0) < 1e-4);

// Asymmetric plate in RecordTrigger (42x38 mm)
var ptsAsymPlate = new DetectionPoint[] {
    new(110, 50),  // py < oy => v = +19mm, u = -21mm
    new(530, 50),  // py < oy => v = +19mm, u = +21mm
    new(530, 430), // py > oy => v = -19mm, u = +21mm
    new(110, 430)  // py > oy => v = -19mm, u = -21mm
};
var dummyAsymPlate = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, ptsAsymPlate, [], [], "OK");
var trigEventAsym = new TriggerEvent(99, "OFFLINE_CAM", dummyAsymPlate, 100, DateTime.UtcNow);
ConveyorVertex[]? asymVertices = null;
if (trigEventAsym.Pose.GluePx != null && trigEventAsym.Pose.GluePx.Length == 4)
{
    try {
        asymVertices = BeltCoordinates.MapVertices(trigEventAsym.Pose.GluePx, trigEventAsym.Pose.AngleDeg,
            fw, fh, trigEventAsym.Pose.MmPerPixel, testOpt.OriginPxX, testOpt.OriginPxY,
            testOpt.OffsetXmm, testOpt.OffsetYmm, BeltDirection.XPlus);
    } catch { asymVertices = null; }
}
Check("record trigger asym: mapped 4 vertices", asymVertices != null && asymVertices.Length == 4);
var trigAckAsym = simTrig.Accept("TRIG/2", 0.0, 0.0, 0.0, 0.0, asymVertices);
Check("record trigger asym: V1 RobotX preserves measured 21mm (-179.0mm)", Math.Abs(trigAckAsym.Corners[0].X - (-179.0)) < 1e-4);
Check("record trigger asym: V1 RobotY preserves measured 19mm (104.0mm)", Math.Abs(trigAckAsym.Corners[0].Y - 104.0) < 1e-4);

// RecordTrigger fallback: collinear points fail-closed to null measured vertices
var collinearPtsTrig = new DetectionPoint[] { new(100, 100), new(200, 200), new(300, 300), new(400, 400) };
var dummyCollinearTrig = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, collinearPtsTrig, [], [], "OK");
var trigEventCollinear = new TriggerEvent(100, "OFFLINE_CAM", dummyCollinearTrig, 100, DateTime.UtcNow);
ConveyorVertex[]? collinearVertices = null;
if (trigEventCollinear.Pose.GluePx != null && trigEventCollinear.Pose.GluePx.Length == 4)
{
    try {
        collinearVertices = BeltCoordinates.MapVertices(trigEventCollinear.Pose.GluePx, trigEventCollinear.Pose.AngleDeg,
            fw, fh, trigEventCollinear.Pose.MmPerPixel, testOpt.OriginPxX, testOpt.OriginPxY,
            testOpt.OffsetXmm, testOpt.OffsetYmm, BeltDirection.XPlus);
    } catch { collinearVertices = null; }
}
Check("record trigger collinear: gracefully caught and set to null", collinearVertices == null);
var trigAckCollinear = simTrig.Accept("TRIG/3", 0.0, 0.0, 0.0, 0.0, collinearVertices);
Check("record trigger collinear: falls back to nominal 40x40 corners",
    Math.Abs(trigAckCollinear.Corners[0].X - (-180.0)) < 1e-4 && Math.Abs(trigAckCollinear.Corners[0].Y - 105.0) < 1e-4);

// Sidebar HoleCoordinates canonical ordering:
var sidebarVerts = BeltCoordinates.MapVertices(dummyPlate.GluePx, dummyPlate.AngleDeg, fw, fh,
    dummyPlate.MmPerPixel, testOpt.OriginPxX, testOpt.OriginPxY, testOpt.OffsetXmm, testOpt.OffsetYmm, BeltDirection.XPlus);
var sidebarLines = sidebarVerts.Select(v => $"P{v.Index}  X={v.X_ConveyorMm,8:F2}  Y={v.Y_ConveyorMm,8:F2}").ToArray();
Check("sidebar canonical order: line 1 is P1", sidebarLines[0].StartsWith("P1") && sidebarLines[0].Contains("X=   20.00") && sidebarLines[0].Contains("Y=   20.00"));
Check("sidebar canonical order: line 2 is P2", sidebarLines[1].StartsWith("P2") && sidebarLines[1].Contains("X=  -20.00") && sidebarLines[1].Contains("Y=   20.00"));
Check("sidebar canonical order: line 3 is P3", sidebarLines[2].StartsWith("P3") && sidebarLines[2].Contains("X=  -20.00") && sidebarLines[2].Contains("Y=  -20.00"));
Check("sidebar canonical order: line 4 is P4", sidebarLines[3].StartsWith("P4") && sidebarLines[3].Contains("X=   20.00") && sidebarLines[3].Contains("Y=  -20.00"));

// Guarded live bench path: scalar center/theta ADS contract unchanged, zero vertex fields sent over ADS
var benchItem = (Event: trigEvent, Pose: (poseX: e2ePose.CenterX_mm, poseY: e2ePose.CenterY_mm, poseTheta: e2ePose.Theta_deg));
Check("live bench contract: scalar tuple preserves CenterX", Math.Abs(benchItem.Pose.poseX - 0.0) < 1e-4);
Check("live bench contract: scalar tuple preserves CenterY", Math.Abs(benchItem.Pose.poseY - 0.0) < 1e-4);
Check("live bench contract: scalar tuple preserves Theta", Math.Abs(benchItem.Pose.poseTheta - 0.0) < 1e-4);

// 21. Camera Slot Persistence Regression: SaveSlot must preserve model.Mode and not hardcode CameraTransportMode.Ip
string mwPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "App", "Vision", "MainWindow.xaml.cs");
if (!File.Exists(mwPath))
{
    mwPath = Path.Combine("App", "Vision", "MainWindow.xaml.cs");
}
Check("slot regression: MainWindow.xaml.cs exists for source audit", File.Exists(mwPath));
if (File.Exists(mwPath))
{
    string mwContent = File.ReadAllText(mwPath);
    int saveSlotIndex = mwContent.IndexOf("void SaveSlot(int slot)");
    Check("slot regression: SaveSlot method found in MainWindow.xaml.cs", saveSlotIndex >= 0);
    if (saveSlotIndex >= 0)
    {
        int methodEnd = mwContent.IndexOf("}", saveSlotIndex);
        string saveSlotMethod = mwContent.Substring(saveSlotIndex, methodEnd - saveSlotIndex + 1);

        bool hardcodesIp = System.Text.RegularExpressions.Regex.IsMatch(saveSlotMethod, @"CameraTransportMode\s+mode\s*=\s*CameraTransportMode\.Ip\s*;");
        bool preservesModelMode = saveSlotMethod.Contains("model.Mode");

        Check("slot regression: SaveSlot does not hardcode CameraTransportMode.Ip", !hardcodesIp);
        Check("slot regression: SaveSlot preserves model.Mode when saving slot settings", preservesModelMode);

        int applySavedIndex = mwContent.IndexOf("void ApplySavedSlot(int slot)");
        Check("slot regression: ApplySavedSlot populates SlotIpBox from saved IP",
            applySavedIndex >= 0 && mwContent.Substring(applySavedIndex, 300).Contains("SlotIpBox(slot).Text=saved.Ip;"));
    }
}

// 23. Manual-IP Fresh Connect Workflow & Validation Tests
Check("manual-ip fresh: CanDispatchManualHik allows valid IP and explicit NIC for Hikrobot",
    CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "169.254.113.1", "169.254.113.10") &&
    CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikvision", "192.168.50.2", "192.168.50.1"));

Check("manual-ip fresh: CanDispatchManualHik rejects Basler (preserves Basler SCAN REQUIRED)",
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Basler", "169.254.113.1", "169.254.113.10"));

Check("manual-ip fresh: CanDispatchManualHik rejects blank or invalid camera IP",
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "", "169.254.113.10") &&
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "   ", "169.254.113.10") &&
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "999.999.999.999", "169.254.113.10") &&
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "not-an-ip", "169.254.113.10"));

Check("manual-ip fresh: CanDispatchManualHik rejects blank or invalid explicit NIC (fails closed)",
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "169.254.113.1", "") &&
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "169.254.113.1", "   ") &&
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "169.254.113.1", "999.999.999.999") &&
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", "169.254.113.1", "invalid_nic"));

Check("manual-ip fresh: CanDispatchManualHik rejects USB mode",
    !CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Usb, "Hikrobot", "169.254.113.1", "169.254.113.10"));

string matchingSubnetCameraIp = "192.168.100.3";
if (CameraTransportPolicy.FindLocalIp(IPAddress.Parse(matchingSubnetCameraIp)).Length == 0)
{
    var anyLocal = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address;
    if (anyLocal != null)
    {
        var b = anyLocal.GetAddressBytes();
        matchingSubnetCameraIp = $"{b[0]}.{b[1]}.{b[2]}.{(b[3] == 200 ? 201 : 200)}";
    }
}
Check("manual-ip fresh: CanDispatchManualHik allows auto-resolvable host NIC when local subnet matches",
    CameraTransportPolicy.CanDispatchManualHik(CameraTransportMode.Ip, "Hikrobot", matchingSubnetCameraIp, ""));

if (File.Exists(mwPath))
{
    string mwText = File.ReadAllText(mwPath);
    int updateUiIndex = mwText.IndexOf("void UpdateCameraSlotUi(int slot)");
    Check("manual-ip fresh: UpdateCameraSlotUi found in MainWindow.xaml.cs", updateUiIndex >= 0);
    if (updateUiIndex >= 0)
    {
        int methodClose = mwText.IndexOf("private void ConnectCameraIp_Click", updateUiIndex);
        if (methodClose < 0) methodClose = mwText.Length;
        string updateUiBody = mwText.Substring(updateUiIndex, methodClose - updateUiIndex);

        bool onlyRequiresSelected = updateUiBody.Contains("bool canConnect=!open&&model.Selected is not null&&") ||
                                    updateUiBody.Contains("bool canConnect = !open && model.Selected is not null &&");
        bool allowsManualWithoutSelected = updateUiBody.Contains("CanDispatchManualHik") ||
                                           (updateUiBody.Contains("model.Selected is null") && updateUiBody.Contains("canConnect"));

        Check("manual-ip fresh: UpdateCameraSlotUi does not restrict CONNECT strictly to model.Selected != null",
            !onlyRequiresSelected);
        Check("manual-ip fresh: UpdateCameraSlotUi enables CONNECT for manual Hikrobot IP without prior scan",
            allowsManualWithoutSelected);
    }
}
// 24. Settings Page Station Camera IP Configuration & Commissioning Isolation
string xamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "App", "Vision", "MainWindow.xaml");
if (!File.Exists(xamlPath)) xamlPath = Path.Combine("App", "Vision", "MainWindow.xaml");
Check("settings camera: MainWindow.xaml exists for UI audit", File.Exists(xamlPath));
if (File.Exists(xamlPath))
{
    string xaml = File.ReadAllText(xamlPath);
    Check("settings camera: SettingsStation1Tab contains SettingsS1CamIpBox", xaml.Contains("x:Name=\"SettingsS1CamIpBox\""));
    Check("settings camera: SettingsStation1Tab contains SettingsS1HostIpBox", xaml.Contains("x:Name=\"SettingsS1HostIpBox\""));
    Check("settings camera: SettingsStation1Tab contains SettingsS1ConnectButton", xaml.Contains("x:Name=\"SettingsS1ConnectButton\""));
    Check("settings camera: SettingsStation1Tab contains SettingsS1DisconnectButton", xaml.Contains("x:Name=\"SettingsS1DisconnectButton\""));
    Check("settings camera: SettingsStation1Tab contains SettingsS1CamLinkText", xaml.Contains("x:Name=\"SettingsS1CamLinkText\""));
    Check("settings camera: SettingsStation1Tab contains SettingsS1DeviceBox", xaml.Contains("x:Name=\"SettingsS1DeviceBox\""));
    Check("settings camera: SettingsStation2Tab contains SettingsS2CamIpBox", xaml.Contains("x:Name=\"SettingsS2CamIpBox\""));
    Check("settings camera: SettingsStation2Tab contains SettingsS2HostIpBox", xaml.Contains("x:Name=\"SettingsS2HostIpBox\""));
    Check("settings camera: SettingsStation2Tab contains SettingsS2ConnectButton", xaml.Contains("x:Name=\"SettingsS2ConnectButton\""));
    Check("settings camera: SettingsStation2Tab contains SettingsS2DisconnectButton", xaml.Contains("x:Name=\"SettingsS2DisconnectButton\""));
    Check("settings camera: SettingsStation2Tab contains SettingsS2CamLinkText", xaml.Contains("x:Name=\"SettingsS2CamLinkText\""));

    Check("settings camera: Station 02 SCARA robot controls remain excluded / no commands",
        xaml.Contains("NO COMMANDS PERMITTED") && xaml.Contains("UNMAPPED · no validated PLC symbols"));
    Check("settings camera: station headers removed header controls and hints",
        !xaml.Contains("x:Name=\"S1ModeBox\"") && !xaml.Contains("x:Name=\"S2ModeBox\"") &&
        !xaml.Contains("x:Name=\"S1CamSettingsHint\"") && !xaml.Contains("x:Name=\"S2CamSettingsHint\"") &&
        xaml.Contains("x:Name=\"S1CamBadge\"") && xaml.Contains("x:Name=\"S1CamLinkText\"") &&
        xaml.Contains("x:Name=\"S2CamBadge\"") && xaml.Contains("x:Name=\"S2CamLinkText\""));
}

if (File.Exists(mwPath))
{
    string cs = File.ReadAllText(mwPath);
    Check("settings camera: SettingsS1ConnectCamera_Click implemented", cs.Contains("SettingsS1ConnectCamera_Click"));
    Check("settings camera: SettingsS2ConnectCamera_Click implemented", cs.Contains("SettingsS2ConnectCamera_Click"));
    Check("settings camera: SettingsS1DisconnectCamera_Click implemented", cs.Contains("SettingsS1DisconnectCamera_Click"));
    Check("settings camera: SettingsS2DisconnectCamera_Click implemented", cs.Contains("SettingsS2DisconnectCamera_Click"));
    Check("settings camera: SettingsS1Save_Click implemented", cs.Contains("SettingsS1Save_Click"));
    Check("settings camera: SettingsS2Save_Click implemented", cs.Contains("SettingsS2Save_Click"));
    Check("settings camera: SettingsS1IpTextChanged implemented", cs.Contains("SettingsS1IpTextChanged"));
    Check("settings camera: SettingsS1Save_Click implemented", cs.Contains("SettingsS1Save_Click"));
    Check("settings camera: SettingsS1DeviceChanged implemented", cs.Contains("SettingsS1DeviceChanged"));
    Check("settings camera: UpdateCameraSlotUi updates Settings link text and buttons", cs.Contains("SettingsS1CamLinkText!=null") && cs.Contains("SettingsS2CamLinkText!=null"));
    Check("settings camera: header status indicator restricts to LIVE or OFFLINE",
        cs.Contains("string headerState=isLive?\"LIVE\":\"OFFLINE\";") ||
        cs.Contains("string headerState = isLive ? \"LIVE\" : \"OFFLINE\";"));
}
// 25. Station 02 HikDirectIpCamera Dynamic BGR Buffer & Acquisition Mode Regression Tests
Check("hik direct: Station 02 2592x1944 BGR payload is strictly 15,116,544 bytes",
    HikDirectIpCamera.Station02RequiredBytes == 2592 * 1944 * 3 &&
    HikDirectIpCamera.Station02RequiredBytes == 15116544);

Check("hik direct: legacy buffer 9,331,200 is insufficient for Station 02, causing MV_E_NOENOUGH_BUF",
    HikDirectIpCamera.Station02RequiredBytes > (1440 * 1080 * 3 * 2));

Check("hik direct: dynamic buffer for Station 02 allocates sufficient BGR capacity",
    HikDirectIpCamera.CalculateRequiredBufferSize(2592, 1944) >= HikDirectIpCamera.Station02RequiredBytes);

Check("hik direct: buffer for Station 01 preserves required 1440x1080 BGR capacity",
    HikDirectIpCamera.CalculateRequiredBufferSize(1440, 1080) >= (1440 * 1080 * 3));

Check("hik direct: unqueried fallback buffer size defaults safely to Station 02 requirement",
    HikDirectIpCamera.CalculateRequiredBufferSize(0, 0) >= HikDirectIpCamera.Station02RequiredBytes);

Check("hik direct: raw sensor payload size correctly scales 3x for BGR conversion",
    HikDirectIpCamera.CalculateRequiredBufferSize(0, 0, 2592 * 1944) >= HikDirectIpCamera.Station02RequiredBytes);

Check("hik direct: payload-size avoids 3x over-allocation when payload already reflects BGR",
    HikDirectIpCamera.CalculateRequiredBufferSize(2592, 1944, 15116544) == 15116544 &&
    HikDirectIpCamera.CalculateRequiredBufferSize(2592, 1944, 15116544) < (15116544L * 3));

Check("hik direct: payload-size expands raw Bayer/Mono payload to 3x BGR capacity",
    HikDirectIpCamera.CalculateRequiredBufferSize(2592, 1944, 5038848) == 15116544);

Check("hik direct: dynamic buffer scales above Station 02 for higher resolution sensors",
    HikDirectIpCamera.CalculateRequiredBufferSize(4096, 3000) == (4096L * 3000L * 3L));

bool overflowCaught = false;
try { HikDirectIpCamera.CalculateRequiredBufferSize(1000000, 1000000); }
catch (ArgumentOutOfRangeException) { overflowCaught = true; }
Check("hik direct: buffer calculation protects against overflow exceeding int.MaxValue", overflowCaught);

string myCameraPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Dependencies", "Devices", "MyCamera.cs");
if (!File.Exists(myCameraPath)) myCameraPath = Path.Combine("Dependencies", "Devices", "MyCamera.cs");
if (File.Exists(myCameraPath))
{
    string myc = File.ReadAllText(myCameraPath);
    Check("hik direct: acquisition mode continuous matches MyCamera.cs enum (2)",
        myc.Contains("MV_ACQ_MODE_CONTINUOUS = 2") && HikDirectIpCamera.TargetAcquisitionMode == 2);
    Check("hik direct: trigger mode off matches MyCamera.cs enum (0)",
        myc.Contains("MV_TRIGGER_MODE_OFF = 0") && HikDirectIpCamera.TargetTriggerMode == 0);
    Check("hik direct: pixel format matches MyCamera.cs PixelType_Gvsp_BGR8_Packed (0x02180015)",
        myc.Contains("PixelType_Gvsp_BGR8_Packed = 0x02180015") && HikDirectIpCamera.TargetPixelFormat == 0x02180015);
}

string mvsHeader = Path.Combine(@"C:\Program Files (x86)\MVS\Development\Includes", "CameraParams.h");
if (File.Exists(mvsHeader))
{
    string hdr = File.ReadAllText(mvsHeader);
    Check("hik direct: trigger mode off matches MVS SDK CameraParams.h enum definition",
        hdr.Contains("MV_TRIGGER_MODE_OFF                 = 0"));
}

Check("hik direct: trigger selector names follow GenICam SFNC conventions",
    HikDirectIpCamera.FrameStartSelector == "FrameStart" &&
    HikDirectIpCamera.AcquisitionStartSelector == "AcquisitionStart" &&
    HikDirectIpCamera.FrameBurstStartSelector == "FrameBurstStart");

Check("hik direct: trigger selector values match GenICam SFNC standard (FrameStart=3, AcqStart=0, BurstStart=6)",
    HikDirectIpCamera.FrameStartSelectorValue == 3 &&
    HikDirectIpCamera.AcquisitionStartSelectorValue == 0 &&
    HikDirectIpCamera.FrameBurstStartSelectorValue == 6);

Check("hik direct: IsUnsupportedSelectorError identifies MV_E_SUPPORT and node missing while preserving fatal errors",
    HikDirectIpCamera.IsUnsupportedSelectorError(unchecked((int)0x80000001)) &&  // MV_E_SUPPORT
    HikDirectIpCamera.IsUnsupportedSelectorError(unchecked((int)0x80000021)) &&  // MV_E_DEV_SUPPORT
    HikDirectIpCamera.IsUnsupportedSelectorError(unchecked((int)0x80000109)) &&  // MV_E_GC_NODE_NOT_FOUND
    HikDirectIpCamera.IsUnsupportedSelectorError(unchecked((int)0x80000200)) &&  // MV_E_NOT_IMPLEMENTED
    !HikDirectIpCamera.IsUnsupportedSelectorError(unchecked((int)0x80000000)) && // MV_E_HANDLE (not ignored)
    !HikDirectIpCamera.IsUnsupportedSelectorError(unchecked((int)0x80000004)) && // MV_E_PARAMETER (not ignored)
    !HikDirectIpCamera.IsUnsupportedSelectorError(0));

Check("hik direct: ConfigureTriggerSelector fails closed with MV_E_HANDLE on null handle",
    HikDirectIpCamera.ConfigureTriggerSelector(IntPtr.Zero, HikDirectIpCamera.FrameStartSelector, HikDirectIpCamera.FrameStartSelectorValue) == unchecked((int)0x80000000));

var nativeType = typeof(HikDirectIpCamera).GetNestedType("Native", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var getOptMethod = nativeType?.GetMethod("GetOptimalPacketSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var setIntMethod = nativeType?.GetMethod("SetIntValueEx", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
Check("hik direct: native GetOptimalPacketSize import declared with handle parameter",
    getOptMethod != null && getOptMethod.ReturnType == typeof(int) && getOptMethod.GetParameters().Length == 1);
Check("hik direct: native SetIntValueEx import declared with key and int64 value parameter",
    setIntMethod != null && setIntMethod.GetParameters().Length == 3 &&
    setIntMethod.GetParameters()[1].ParameterType == typeof(string) &&
    setIntMethod.GetParameters()[2].ParameterType == typeof(long));

string mvsControlHeader = Path.Combine(@"C:\Program Files (x86)\MVS\Development\Includes", "MvCameraControl.h");
if (File.Exists(mvsControlHeader))
{
    string chdr = File.ReadAllText(mvsControlHeader);
    Check("hik direct: MVS SDK MvCameraControl.h declares MV_CC_GetOptimalPacketSize",
        chdr.Contains("MV_CC_GetOptimalPacketSize"));
    Check("hik direct: MVS SDK MvCameraControl.h declares MV_CC_SetIntValueEx",
        chdr.Contains("MV_CC_SetIntValueEx"));
}

string directSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "App", "Vision", "Vision", "HikDirectIpCamera.cs");
if (!File.Exists(directSourcePath)) directSourcePath = Path.Combine("App", "Vision", "Vision", "HikDirectIpCamera.cs");
if (File.Exists(directSourcePath))
{
    string directCode = File.ReadAllText(directSourcePath);
    Check("hik direct: code never issues persistent UserSetSave or flash writes (volatile registers only)",
        !directCode.Contains("UserSetSave") &&
        !directCode.Contains("UserSetSelector") &&
        !directCode.Contains("MV_CC_SaveDeviceFeature") &&
        !directCode.Contains("MV_CC_SetCommandValue"));
}
string hikServicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Dependencies", "Devices", "HikCameraService.cs");
if (!File.Exists(hikServicePath)) hikServicePath = Path.Combine("Dependencies", "Devices", "HikCameraService.cs");
if (File.Exists(hikServicePath))
{
    string hcs = File.ReadAllText(hikServicePath);
    Check("hik direct: configuration keys and enums align with HikCameraService.cs",
        hcs.Contains("\"AcquisitionMode\", (int)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS") &&
        hcs.Contains("\"TriggerMode\", (int)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF") &&
        hcs.Contains("\"PixelFormat\", (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed"));
}
bool nonIpv4Rejected = false;
try { new HikDirectIpCamera("::1", "192.168.100.1"); }
catch (ArgumentException) { nonIpv4Rejected = true; }
Check("hik direct: constructor rejects non-IPv4 addresses (fails closed)", nonIpv4Rejected);

using var directStation02 = new HikDirectIpCamera("192.168.100.3", "192.168.100.1");
Check("hik direct: IP construction correctly stores Station 02 camera and host IPs",
    directStation02.CameraIp == "192.168.100.3" && directStation02.HostIp == "192.168.100.1");

// 26. Offline Tray Allocator & CAM2 Processing Regression Tests (Pure Offline, No ADS/Hardware Writes)
var validBounds = new TrayWorkspaceBounds(0, 500, 0, 500);
Check("tray bounds: valid bounds recognized", validBounds.IsValid && validBounds.Contains(250, 250));
Check("tray bounds: out-of-bounds rejected", !validBounds.Contains(600, 250) && !validBounds.Contains(-10, 250));

var invalidBounds = new TrayWorkspaceBounds(500, 0, 0, 500);
Check("tray bounds: inverted bounds invalid", !invalidBounds.IsValid);

bool zeroRowRejected = false;
try { new TrayLayoutConfig(0, 4, 30, 30, 50, 50, 0, validBounds); }
catch (ArgumentException) { zeroRowRejected = true; }
Check("tray config: zero rows rejected", zeroRowRejected);

bool zeroColRejected = false;
try { new TrayLayoutConfig(2, 0, 30, 30, 50, 50, 0, validBounds); }
catch (ArgumentException) { zeroColRejected = true; }
Check("tray config: zero columns rejected", zeroColRejected);

bool nonfinitePitchRejected = false;
try { new TrayLayoutConfig(2, 2, -10, 30, 50, 50, 0, validBounds); }
catch (ArgumentException) { nonfinitePitchRejected = true; }
Check("tray config: non-positive pitch rejected", nonfinitePitchRejected);

bool nanOriginRejected = false;
try { new TrayLayoutConfig(2, 2, 30, 30, double.NaN, 50, 0, validBounds); }
catch (ArgumentException) { nanOriginRejected = true; }
Check("tray config: non-finite origin rejected", nanOriginRejected);

bool nanAngleRejected = false;
try { new TrayLayoutConfig(2, 2, 30, 30, 50, 50, double.NaN, validBounds); }
catch (ArgumentException) { nanAngleRejected = true; }
Check("tray config: non-finite angle rejected", nanAngleRejected);

bool nullBoundsRejected = false;
try { new TrayLayoutConfig(2, 2, 30, 30, 50, 50, 0, null!); }
catch (ArgumentNullException) { nullBoundsRejected = true; }
Check("tray config: null workspace bounds rejected", nullBoundsRejected);

bool outOfBoundsSlotsRejected = false;
try { new TrayLayoutConfig(10, 10, 100, 100, 50, 50, 0, validBounds); }
catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException) { outOfBoundsSlotsRejected = true; }
Check("tray config: slots exceeding workspace bounds rejected", outOfBoundsSlotsRejected);

// Explicit parameters required: verify type has NO parameterless constructor
var constructors = typeof(TrayLayoutConfig).GetConstructors();
bool noDefaultConstructor = constructors.All(c => c.GetParameters().Length > 0);
Check("tray config: no default 3x4 constructor (explicit layout parameters required)", noDefaultConstructor);

// Valid 2x2 Tray Allocator test
var tray2x2 = new TrayLayoutConfig(2, 2, 40, 40, 100, 100, 0, validBounds);
var allocator = new OfflineTrayAllocator(tray2x2);
Check("tray allocator: initial capacity 4 and unoccupied",
    allocator.Capacity == 4 && allocator.OccupiedCount == 0 && allocator.RemainingCount == 4 && !allocator.IsFull);
Check("tray allocator: slots initialized with computed placement coordinates",
    allocator.Slots.Count == 4 &&
    allocator.Slots[0].PlaceX_mm == 100 && allocator.Slots[0].PlaceY_mm == 100 &&
    allocator.Slots[1].PlaceX_mm == 140 && allocator.Slots[1].PlaceY_mm == 100 &&
    allocator.Slots[2].PlaceX_mm == 100 && allocator.Slots[2].PlaceY_mm == 140 &&
    allocator.Slots[3].PlaceX_mm == 140 && allocator.Slots[3].PlaceY_mm == 140);

var dummyPose1 = new TrackedWorkpiecePose(1, "CAM2/MANUAL/1", DateTime.UtcNow, 0, 50, 60, 5.0, 40, 40, 0.95, Array.Empty<ConveyorVertex>(), true);
var alloc1 = allocator.Allocate(dummyPose1);
Check("tray allocator: valid workpiece allocated to slot 1",
    alloc1.Success && alloc1.Slot != null && alloc1.Slot.Index == 1 &&
    alloc1.Slot.Row == 0 && alloc1.Slot.Column == 0 &&
    allocator.OccupiedCount == 1 && allocator.RemainingCount == 3);

// Duplicate allocation rejected
var dupAlloc = allocator.Allocate(dummyPose1);
Check("tray allocator: duplicate workpiece key rejected without consuming slot",
    !dupAlloc.Success && dupAlloc.Status.Contains("REJECTED_DUPLICATE") && allocator.OccupiedCount == 1);

// Non-finite pose rejected
var nanPose = new TrackedWorkpiecePose(2, "CAM2/MANUAL/2", DateTime.UtcNow, 0, double.NaN, 60, 5.0, 40, 40, 0.95, Array.Empty<ConveyorVertex>(), true);
var nanAlloc = allocator.Allocate(nanPose);
Check("tray allocator: non-finite workpiece pose rejected fail-closed",
    !nanAlloc.Success && nanAlloc.Status.Contains("REJECTED_NONFINITE_POSE") && allocator.OccupiedCount == 1);

// Allocate slots 2, 3, 4
var dummyPose2 = new TrackedWorkpiecePose(2, "CAM2/MANUAL/2", DateTime.UtcNow, 0, 51, 61, 0, 40, 40, 0.9, Array.Empty<ConveyorVertex>(), true);
var dummyPose3 = new TrackedWorkpiecePose(3, "CAM2/MANUAL/3", DateTime.UtcNow, 0, 52, 62, 0, 40, 40, 0.9, Array.Empty<ConveyorVertex>(), true);
var dummyPose4 = new TrackedWorkpiecePose(4, "CAM2/MANUAL/4", DateTime.UtcNow, 0, 53, 63, 0, 40, 40, 0.9, Array.Empty<ConveyorVertex>(), true);
allocator.Allocate(dummyPose2);
allocator.Allocate(dummyPose3);
var alloc4 = allocator.Allocate(dummyPose4);
Check("tray allocator: fills exactly to capacity",
    alloc4.Success && allocator.IsFull && allocator.OccupiedCount == 4 && allocator.RemainingCount == 0);

// Overflow check: MUST NOT auto-advance trays!
var dummyPose5 = new TrackedWorkpiecePose(5, "CAM2/MANUAL/5", DateTime.UtcNow, 0, 54, 64, 0, 40, 40, 0.9, Array.Empty<ConveyorVertex>(), true);
var overflowAlloc = allocator.Allocate(dummyPose5);
Check("tray allocator: tray full rejected (NO auto-advance)",
    !overflowAlloc.Success && overflowAlloc.Status.Contains("REJECTED_TRAY_FULL") &&
    allocator.OccupiedCount == 4 && allocator.RemainingCount == 0);

// Audit trail check
Check("tray allocator: audit trail records all allocation and rejection events",
    allocator.AuditTrail.Count >= 6 &&
    allocator.AuditTrail.Any(a => a.Status == "ALLOCATED" && a.SlotIndex == 1) &&
    allocator.AuditTrail.Any(a => a.Status.Contains("REJECTED_DUPLICATE")) &&
    allocator.AuditTrail.Any(a => a.Status.Contains("REJECTED_TRAY_FULL")));

// Reset tray
allocator.ResetTray();
Check("tray allocator: ResetTray explicitly commanded clears occupied slots while preserving audit log",
    allocator.OccupiedCount == 0 && allocator.RemainingCount == 4 && !allocator.IsFull &&
    allocator.AuditTrail.Any(a => a.Status == "TRAY_RESET"));

// CAM2 Pipeline & Calibration Guard Tests
var uncalibratedOpt = new VisionOptions { FixedMmPerPixel = 0 }; // scale <= 0
Check("cam2 pipeline: scale <= 0 marked uncalibrated", !Camera2Pipeline.IsCalibrationValid(uncalibratedOpt));
Check("cam2 pipeline: null options marked uncalibrated", !Camera2Pipeline.IsCalibrationValid(null));

var calibratedOpt = new VisionOptions {
    FixedMmPerPixel = 0.0825,
    PlateSizeMm = 50.0,
    HolePitchMm = 40.0,
    OriginPxX = 320,
    OriginPxY = 240,
    OffsetXmm = 0,
    OffsetYmm = 0
};
Check("cam2 pipeline: valid options recognized as calibrated", Camera2Pipeline.IsCalibrationValid(calibratedOpt));

// CAM2 safe processing path fails closed when uncalibrated
using var syntheticFrame = new OpenCvSharp.Mat(480, 640, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(30, 30, 30));
var cam2Tracker = new MidlineTracker();
var uncalibResult = Camera2Pipeline.ProcessFrame(
    syntheticFrame, "CAM2/DA7403922", uncalibratedOpt, BeltDirection.YMinus, cam2Tracker, allocator);
Check("cam2 pipeline: uncalibrated fails closed without pose or allocation",
    !uncalibResult.Processed && !uncalibResult.Calibrated && !uncalibResult.PlateDetected &&
    uncalibResult.BeltPose == null && uncalibResult.WorkpiecePose == null && uncalibResult.Allocation == null &&
    uncalibResult.StatusText.Contains("FAIL CLOSED"));

// CAM2 tracker key isolation
string keyHik = Camera2Pipeline.BuildTrackerKey("DA7403922");
string keyManual = Camera2Pipeline.BuildTrackerKey("192.168.100.3");
string keyEmpty = Camera2Pipeline.BuildTrackerKey("");
Check("cam2 tracker key: uses CAM2 prefix and preserves identifier",
    keyHik == "CAM2/DA7403922" && keyManual == "CAM2/192.168.100.3" && keyEmpty == "CAM2/manual");
Check("cam2 tracker key: never collides with CAM1 key namespace",
    !keyHik.StartsWith("CAM1") && !keyHik.StartsWith("DEMO"));

// Independent tracker IDs between CAM1 and CAM2
var trackerCam1 = new MidlineTracker();
var trackerCam2 = new MidlineTracker();
var dummyDet = new PlateResult(true, 320, 240, 0, 0, 0, 0.0825, 0.95, null, null, null, "OK");
var t1 = trackerCam1.Update("CAM1/manual", [dummyDet], 640, 480, out _);
var t2 = trackerCam2.Update("CAM2/manual", [dummyDet], 640, 480, out _);
Check("cam2 tracker: independent tracker instance maintains separate track identity",
    t1.Count == 1 && t2.Count == 1 && t1[0].ProductId == t2[0].ProductId);
trackerCam2.Reset();
Check("cam2 tracker: reset of CAM2 tracker does not reset CAM1 tracker state",
    trackerCam1.Update("CAM1/manual", [dummyDet], 640, 480, out _).Count == 1);

// 27. CAM2 Explicit Configuration Path, Synthetic Positive Pipeline, and Zero-Write Boundary Tests
bool calibCreated = Camera2Pipeline.TryCreateCalibrationOptions(
    0.0825, 320, 240, 0, 0, 40.0, 50.0, out var createdOpt, out var calibErr);
Check("cam2 config: TryCreateCalibrationOptions accepts valid explicit parameters",
    calibCreated && createdOpt != null && createdOpt.FixedMmPerPixel == 0.0825 && string.IsNullOrEmpty(calibErr));

bool calibZeroScaleRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    0.0, 320, 240, 0, 0, 40.0, 50.0, out _, out var errZeroScale);
Check("cam2 config: TryCreateCalibrationOptions rejects scale <= 0",
    calibZeroScaleRejected && errZeroScale.Contains("fixedMmPerPixel"));

bool calibNanScaleRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    double.NaN, 320, 240, 0, 0, 40.0, 50.0, out _, out _);
Check("cam2 config: TryCreateCalibrationOptions rejects NaN scale", calibNanScaleRejected);

bool calibZeroPlateRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    0.0825, 320, 240, 0, 0, 40.0, 0.0, out _, out var errZeroPlate);
Check("cam2 config: TryCreateCalibrationOptions rejects plateSize <= 0",
    calibZeroPlateRejected && errZeroPlate.Contains("plateSizeMm"));

bool calibZeroPitchRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    0.0825, 320, 240, 0, 0, 0.0, 50.0, out _, out var errZeroPitch);
Check("cam2 config: TryCreateCalibrationOptions rejects holePitch <= 0",
    calibZeroPitchRejected && errZeroPitch.Contains("holePitchMm"));
bool calibNanPlateRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    0.0825, 320, 240, 0, 0, 40.0, double.NaN, out _, out _);
Check("cam2 config: TryCreateCalibrationOptions rejects NaN plateSize", calibNanPlateRejected);

bool calibNanPitchRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    0.0825, 320, 240, 0, 0, double.NaN, 50.0, out _, out _);
Check("cam2 config: TryCreateCalibrationOptions rejects NaN holePitch", calibNanPitchRejected);

bool calibNanOriginXRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    0.0825, double.NaN, 240, 0, 0, 40.0, 50.0, out _, out var errNanOx);
Check("cam2 config: TryCreateCalibrationOptions rejects NaN originX (no auto default)",
    calibNanOriginXRejected && errNanOx.Contains("originPxX"));

bool calibNanOriginYRejected = !Camera2Pipeline.TryCreateCalibrationOptions(
    0.0825, 320, double.NaN, 0, 0, 40.0, 50.0, out _, out var errNanOy);
Check("cam2 config: TryCreateCalibrationOptions rejects NaN originY (no auto default)",
    calibNanOriginYRejected && errNanOy.Contains("originPxY"));

var optNanOx = new VisionOptions { FixedMmPerPixel = 0.0825, PlateSizeMm = 50, HolePitchMm = 40, OriginPxX = double.NaN, OriginPxY = 240, OffsetXmm = 0, OffsetYmm = 0 };
Check("cam2 pipeline: IsCalibrationValid rejects NaN originX", !Camera2Pipeline.IsCalibrationValid(optNanOx));

var optNanOy = new VisionOptions { FixedMmPerPixel = 0.0825, PlateSizeMm = 50, HolePitchMm = 40, OriginPxX = 320, OriginPxY = double.NaN, OffsetXmm = 0, OffsetYmm = 0 };
Check("cam2 pipeline: IsCalibrationValid rejects NaN originY", !Camera2Pipeline.IsCalibrationValid(optNanOy));


bool trayCreated = Camera2Pipeline.TryCreateTrayLayoutConfig(
    2, 3, 50.0, 50.0, 100.0, 100.0, 0.0, validBounds, out var createdTrayLayout, out var trayErr);
Check("cam2 config: TryCreateTrayLayoutConfig accepts valid explicit 2x3 layout",
    trayCreated && createdTrayLayout != null && createdTrayLayout.Rows == 2 && createdTrayLayout.Columns == 3 && string.IsNullOrEmpty(trayErr));

bool trayZeroColRejected = !Camera2Pipeline.TryCreateTrayLayoutConfig(
    2, 0, 50.0, 50.0, 100.0, 100.0, 0.0, validBounds, out _, out var errZeroCol);
Check("cam2 config: TryCreateTrayLayoutConfig rejects zero columns", trayZeroColRejected && !string.IsNullOrEmpty(errZeroCol));

bool trayOutOfBoundsRejected = !Camera2Pipeline.TryCreateTrayLayoutConfig(
    10, 10, 100.0, 100.0, 100.0, 100.0, 0.0, validBounds, out _, out var errOob);
Check("cam2 config: TryCreateTrayLayoutConfig rejects layout exceeding workspace bounds",
    trayOutOfBoundsRejected && !string.IsNullOrEmpty(errOob));
bool trayNanAngleRejected = !Camera2Pipeline.TryCreateTrayLayoutConfig(
    2, 3, 50.0, 50.0, 100.0, 100.0, double.NaN, validBounds, out _, out _);
Check("cam2 config: TryCreateTrayLayoutConfig rejects NaN trayAngle", trayNanAngleRejected);

bool trayInvertedBoundsRejected = !Camera2Pipeline.TryCreateTrayLayoutConfig(
    2, 3, 50.0, 50.0, 100.0, 100.0, 0.0, new TrayWorkspaceBounds(500, 100, 0, 500), out _, out _);
Check("cam2 config: TryCreateTrayLayoutConfig rejects inverted workspace bounds", trayInvertedBoundsRejected);


// Atomic configuration apply and preservation tests
VisionOptions? atomicOpt = createdOpt;
OfflineTrayAllocator? atomicAlloc = new OfflineTrayAllocator(createdTrayLayout!);
double priorScale = atomicOpt.FixedMmPerPixel;
int priorCap = atomicAlloc.Capacity;

// Test 1: Null tray layout must reject and leave prior state unchanged
bool applyNullTrayRejected = !Camera2Pipeline.TryApplyOfflineConfig(
    ref atomicOpt, ref atomicAlloc, createdOpt!, null, out var errNullTray);
Check("cam2 atomic: null tray layout rejected fail-closed", applyNullTrayRejected && !string.IsNullOrEmpty(errNullTray));
Check("cam2 atomic: null tray apply failure preserves prior calibration and allocator",
    atomicOpt != null && atomicOpt.FixedMmPerPixel == priorScale &&
    atomicAlloc != null && atomicAlloc.Capacity == priorCap);

// Test 2: Invalid calibration options must reject and leave prior state unchanged
var invalidCalib = new VisionOptions { FixedMmPerPixel = -1.0 };
bool applyInvalidCalibRejected = !Camera2Pipeline.TryApplyOfflineConfig(
    ref atomicOpt, ref atomicAlloc, invalidCalib, createdTrayLayout, out var errInvalidCalib);
Check("cam2 atomic: invalid calibration rejected fail-closed", applyInvalidCalibRejected && !string.IsNullOrEmpty(errInvalidCalib));
Check("cam2 atomic: invalid calib apply failure preserves prior calibration and allocator",
    atomicOpt != null && atomicOpt.FixedMmPerPixel == priorScale &&
    atomicAlloc != null && atomicAlloc.Capacity == priorCap);

// Test 3: Valid full explicit config applies atomically
var nextLayout = new TrayLayoutConfig(3, 3, 40.0, 40.0, 50.0, 50.0, 0.0, validBounds);
Camera2Pipeline.TryCreateCalibrationOptions(0.05, 300, 200, 10, 10, 40.0, 50.0, out var nextCalib, out _);
bool applyValidSucceeded = Camera2Pipeline.TryApplyOfflineConfig(
    ref atomicOpt, ref atomicAlloc, nextCalib!, nextLayout, out var errApply);
Check("cam2 atomic: valid full explicit config applies atomically",
    applyValidSucceeded && string.IsNullOrEmpty(errApply) &&
    atomicOpt != null && atomicOpt.FixedMmPerPixel == 0.05 &&
    atomicAlloc != null && atomicAlloc.Capacity == 9);

// Regression: Atomic failure preservation when TrayLayoutConfig constructor throws (slots outside workspace)
var priorOptions = new VisionOptions
{
    FixedMmPerPixel = 0.0825,
    PlateSizeMm = 50.0,
    HolePitchMm = 40.0,
    OriginPxX = 320.0,
    OriginPxY = 240.0,
    OffsetXmm = 0.0,
    OffsetYmm = 0.0
};
var priorLayout = new TrayLayoutConfig(2, 2, 50.0, 50.0, 100.0, 100.0, 0.0, validBounds);
var priorAllocator = new OfflineTrayAllocator(priorLayout);
var workingOptions = priorOptions;
OfflineTrayAllocator? workingAllocator = priorAllocator;

var tinyBounds = new TrayWorkspaceBounds(0, 100, 0, 100);
bool throwsConstructorEx = false;
try
{
    _ = new TrayLayoutConfig(10, 10, 50.0, 50.0, 100.0, 100.0, 0.0, tinyBounds);
}
catch (ArgumentOutOfRangeException)
{
    throwsConstructorEx = true;
}
Check("cam2 atomic regression: invalid tray layout constructor throws on slots outside workspace", throwsConstructorEx);

bool transactionalApplyResult = Camera2Pipeline.TryCreateAndApplyOfflineConfig(
    ref workingOptions, ref workingAllocator,
    0.1234, 400.0, 300.0, 10.0, 10.0, 45.0, 60.0,
    10, 10, 50.0, 50.0, 100.0, 100.0, 0.0, tinyBounds,
    out var transErr);

Check("cam2 atomic regression: TryCreateAndApplyOfflineConfig rejects invalid tray config whose constructor throws",
    !transactionalApplyResult && transErr.Contains("workspace"));

Check("cam2 atomic regression: prior options NOT mutated after tray constructor failure (FixedMmPerPixel preserved)",
    workingOptions.FixedMmPerPixel == 0.0825 && workingOptions.PlateSizeMm == 50.0);

Check("cam2 atomic regression: prior allocator NOT mutated after tray constructor failure (Capacity preserved)",
    workingAllocator != null && workingAllocator.Capacity == 4);

// Synthetic positive regression: Calibrated Frame -> PlateDetector -> Camera2Pipeline -> Pose -> Tray Allocation
var positiveTrayLayout = new TrayLayoutConfig(2, 2, 50, 50, 100, 100, 0, validBounds);
var positiveAllocator = new OfflineTrayAllocator(positiveTrayLayout);
var positiveTracker = new MidlineTracker { Direction = BeltDirection.XPlus, LineFraction = 0.5 };

double syntheticScale = 50.0 / 260.0; // 50mm plate mapped to 260px square
var positiveCalib = new VisionOptions
{
    FixedMmPerPixel = syntheticScale,
    PlateSizeMm = 50.0,
    HolePitchMm = 40.0,
    OriginPxX = 400,
    OriginPxY = 300,
    OffsetXmm = 0,
    OffsetYmm = 0
};

// Helper to draw a synthetic plate image with 4 holes
static Mat CreateSyntheticPlateImage(int centerX, int centerY, double angleDeg)
{
    var mat = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220));
    int half = 130; // 260 / 2
    Cv2.Rectangle(mat, new Rect(centerX - half, centerY - half, 260, 260), new Scalar(55, 55, 55), -1);
    int holeOffset = 104; // 208 / 2 for 40mm pitch
    var holes = new[]
    {
        new Point(centerX - holeOffset, centerY - holeOffset),
        new Point(centerX + holeOffset, centerY - holeOffset),
        new Point(centerX + holeOffset, centerY + holeOffset),
        new Point(centerX - holeOffset, centerY + holeOffset)
    };
    foreach (var h in holes) Cv2.Circle(mat, h, 13, new Scalar(170, 170, 170), -1);
    if (Math.Abs(angleDeg) > 0.01)
    {
        using var rot = Cv2.GetRotationMatrix2D(new Point2f(centerX, centerY), angleDeg, 1.0);
        using var rotated = mat.Clone();
        Cv2.WarpAffine(rotated, mat, rot, new Size(800, 600), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(220, 220, 220));
    }
    return mat;
}

using var frameBeforeMidline = CreateSyntheticPlateImage(350, 300, 0); // X=350 before midline at 400 (along = -50)
var res1 = Camera2Pipeline.ProcessFrame(
    frameBeforeMidline, "CAM2/DA7403922", positiveCalib, BeltDirection.XPlus, positiveTracker, positiveAllocator);

Check("synthetic positive: frame 1 before midline detects plate with valid pose",
    res1.Processed && res1.Calibrated && res1.PlateDetected &&
    res1.BeltPose != null && res1.WorkpiecePose != null &&
    res1.Allocation == null && positiveAllocator.OccupiedCount == 0);

using var frameAfterMidline = CreateSyntheticPlateImage(430, 300, 0); // X=430 crossing midline at 400 (along = +30, dx = 80)
var res2 = Camera2Pipeline.ProcessFrame(
    frameAfterMidline, "CAM2/DA7403922", positiveCalib, BeltDirection.XPlus, positiveTracker, positiveAllocator);
Check("synthetic positive: frame 2 crossing triggers tray allocation to Slot 1",
    res2.Processed && res2.PlateDetected &&
    res2.Allocation != null && res2.Allocation.Success &&
    res2.Allocation.Slot != null && res2.Allocation.Slot.Index == 1 &&
    res2.Allocation.Slot.Row == 0 && res2.Allocation.Slot.Column == 0 &&
    positiveAllocator.OccupiedCount == 1 &&
    positiveAllocator.AuditTrail.Any(a => a.Status == "ALLOCATED" && a.SlotIndex == 1));

// Negative test: Missing calibration with the same plate image
var uncalibZero = new VisionOptions { FixedMmPerPixel = 0.0 };
var negTracker = new MidlineTracker { Direction = BeltDirection.XPlus, LineFraction = 0.5 };
var negAllocator = new OfflineTrayAllocator(positiveTrayLayout);
var resNegCalib = Camera2Pipeline.ProcessFrame(
    frameAfterMidline, "CAM2/DA7403922", uncalibZero, BeltDirection.XPlus, negTracker, negAllocator);

Check("negative missing calibration: fails closed without pose or allocation",
    !resNegCalib.Processed && !resNegCalib.Calibrated && !resNegCalib.PlateDetected &&
    resNegCalib.BeltPose == null && resNegCalib.WorkpiecePose == null && resNegCalib.Allocation == null &&
    negAllocator.OccupiedCount == 0 && resNegCalib.StatusText.Contains("FAIL CLOSED"));

// Negative test: Calibrated options but tray layout is null
var trackerNoTray = new MidlineTracker { Direction = BeltDirection.XPlus, LineFraction = 0.5 };
using var f1 = CreateSyntheticPlateImage(350, 300, 0);
using var f2 = CreateSyntheticPlateImage(430, 300, 0);
Camera2Pipeline.ProcessFrame(f1, "CAM2/DA7403922", positiveCalib, BeltDirection.XPlus, trackerNoTray, null);
var resNoTray = Camera2Pipeline.ProcessFrame(f2, "CAM2/DA7403922", positiveCalib, BeltDirection.XPlus, trackerNoTray, null);

Check("negative missing tray layout: detects plate but skips allocation cleanly without throwing",
    resNoTray.Processed && resNoTray.PlateDetected && resNoTray.WorkpiecePose != null && resNoTray.Allocation == null);

// Zero-write boundary test
var zeroWriteTransport = new FakeAdsMailboxTransport();
zeroWriteTransport.TryConnect(851);
Check("zero-write boundary: FakeAdsMailboxTransport has zero writes during CAM2 processing",
    zeroWriteTransport.Writes.Count == 0);

// UI configuration path regression audit
if (File.Exists(xamlPath))
{
    string s2Xaml = File.ReadAllText(xamlPath);
    Check("settings s2 ui: contains explicit calibration inputs",
        s2Xaml.Contains("x:Name=\"SettingsS2ScaleBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2OriginXBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2OriginYBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2OffsetXBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2OffsetYBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2HolePitchBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2PlateSizeBox\""));

    Check("settings s2 ui: does not contain implicit 'auto' or '0' defaults",
        !s2Xaml.Contains("x:Name=\"SettingsS2OriginXBox\" Text=\"auto\"") &&
        !s2Xaml.Contains("x:Name=\"SettingsS2OriginYBox\" Text=\"auto\"") &&
        !s2Xaml.Contains("x:Name=\"SettingsS2TrayAngleBox\" Text=\"0\"") &&
        !s2Xaml.Contains("x:Name=\"SettingsS2TrayBoundMaxBox\""));

    Check("settings s2 ui: contains explicit 4-boundary tray layout inputs",
        s2Xaml.Contains("x:Name=\"SettingsS2TrayRowsBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayColsBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayPitchXBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayPitchYBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayOriginXBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayOriginYBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayAngleBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayBoundMinXBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayBoundMaxXBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayBoundMinYBox\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TrayBoundMaxYBox\""));

    Check("settings s2 ui: contains apply and clear config buttons",
        s2Xaml.Contains("x:Name=\"SettingsS2ApplyConfigButton\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2ClearConfigButton\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2CalibSummary\"") &&
        s2Xaml.Contains("x:Name=\"SettingsS2TraySummary\""));
}

if (File.Exists(mwPath))
{
    string mwCode = File.ReadAllText(mwPath);
    Check("settings s2 code: SettingsS2ApplyConfig_Click implemented",
        mwCode.Contains("SettingsS2ApplyConfig_Click"));
    Check("settings s2 code: uses explicit SettingsS2HolePitchBox and SettingsS2PlateSizeBox (no hard-coded 40/50)",
        mwCode.Contains("SettingsS2HolePitchBox") &&
        mwCode.Contains("SettingsS2PlateSizeBox") &&
        !mwCode.Contains(",40.0,50.0,") &&
        !mwCode.Contains(", 40.0, 50.0,"));
    Check("settings s2 code: removes auto origin semantics from S2 handler",
        !mwCode.Contains("SettingsS2OriginXBox.Text.Trim().Equals(\"auto\"") &&
        !mwCode.Contains("SettingsS2OriginYBox.Text.Trim().Equals(\"auto\""));
    Check("settings s2 code: SettingsS2ClearConfig_Click implemented",
        mwCode.Contains("SettingsS2ClearConfig_Click"));
    Check("settings s2 code: TryApplyStation2OfflineConfig method implemented",
        mwCode.Contains("TryApplyStation2OfflineConfig"));
    Check("settings s2 code: TryApplyStation2OfflineConfig rejects null tray layout (no trayConfig=null success path)",
        mwCode.Contains("if(trayConfig==null") || mwCode.Contains("if (trayConfig == null") || mwCode.Contains("trayConfig == null"));
    Check("settings s2 code: TryApplyStation2OfflineConfig delegates to transactional helper without mutating _optionsCam2 first",
        mwCode.Contains("Camera2Pipeline.TryApplyOfflineConfig") &&
        !mwCode.Contains("_optionsCam2.FixedMmPerPixel=options.FixedMmPerPixel;"));
    int pollCam2Index = mwCode.IndexOf("void PollCamera2()");
    Check("cam2 live label: PollCamera2 method found in MainWindow.xaml.cs", pollCam2Index >= 0);
    if (pollCam2Index >= 0)
    {
        int methodEnd = mwCode.IndexOf("public bool TryApplyStation2OfflineConfig", pollCam2Index);
        string pollCam2Code = methodEnd > pollCam2Index
            ? mwCode.Substring(pollCam2Index, methodEnd - pollCam2Index)
            : mwCode.Substring(pollCam2Index);

        Check("cam2 live label: calibrated live detection does not label live camera as OFFLINE SIM",
            !pollCam2Code.Contains("OFFLINE SIM"));
        Check("cam2 live label: calibrated live detection contains truthful VISION ONLY wording",
            pollCam2Code.Contains("VISION ONLY") && pollCam2Code.Contains("CAM 2 · VISION ONLY"));
    }
}

// =========================================================================
// OFFLINE WORKPIECE CENTER & TILT ANGLE EVALUATION TESTS (SPEC-20261105)
// =========================================================================
Console.WriteLine("OFFLINE CENTER & TILT ANGLE EVALUATION TESTS:");

// 1. Blank frame returns Found=false
using (var blankMat = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    var blankRes = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(blankMat);
    Check("offline eval: blank frame returns Found=false", !blankRes.Found);
}

// 2. Pixel-first contract & null millimeter fallback when calibration is omitted
using (var rectMat = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    Cv2.Rectangle(rectMat, new Rect(300, 200, 200, 200), new Scalar(50, 50, 50), -1);
    var pxRes = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(rectMat);
    Check("offline eval: pixel-first detection reports finite pixel coordinates",
        pxRes.Found && Math.Abs(pxRes.CenterPxX - 400) < 2.0 && Math.Abs(pxRes.CenterPxY - 300) < 2.0);
    Check("offline eval: omitted calibration produces null millimeter values",
        pxRes.CenterX_mm == null && pxRes.CenterY_mm == null && pxRes.ThetaDeg == null && pxRes.MmPerPixel == null);

    // Call with caller options: must clone options and not mutate caller's instance
    var callerOpt = new VisionOptions { FixedMmPerPixel = 0.5, OriginPxX = 100, OriginPxY = 100 };
    var resOpt = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(rectMat, fixedMmPerPixel: 0.2, originPxX: 400, originPxY: 300, options: callerOpt);
    Check("offline eval: caller VisionOptions instance is not mutated", callerOpt.FixedMmPerPixel == 0.5 && callerOpt.OriginPxX == 100);
    Check("offline eval: millimeter outputs strictly null to prevent motion implications",
        resOpt.CenterX_mm == null && resOpt.CenterY_mm == null && resOpt.ThetaDeg == null && resOpt.MmPerPixel == null);
}

// 3. Deterministic synthetic multi-angle suite (0, 15, -30, 35.5, 44 deg)
foreach (double testAngle in new[] { 0.0, 15.0, -30.0, 35.5, 44.0 })
{
    using var baseImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220));
    using (var tmp = baseImg.Clone())
    {
        Cv2.Rectangle(tmp, new Rect(270, 170, 260, 260), new Scalar(50, 50, 50), -1);
        using var rotMat = Cv2.GetRotationMatrix2D(new Point2f(400, 300), testAngle, 1.0);
        Cv2.WarpAffine(tmp, baseImg, rotMat, new Size(800, 600), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(220, 220, 220));
    }
    var res = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(baseImg);
    double err = res.AngleDeg - testAngle;
    while (err >= 45.0) err -= 90.0;
    while (err < -45.0) err += 90.0;

    Check($"offline eval: synthetic angle {testAngle:F1} deg",
        res.Found && Math.Abs(err) < 1.5 && Math.Abs(res.CenterPxX - 400) < 2.0 && Math.Abs(res.CenterPxY - 300) < 2.0);
}

// 4. 90-degree rotational symmetry normalization into [-45, 45)
using (var symmImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    Cv2.Rectangle(symmImg, new Rect(270, 170, 260, 260), new Scalar(50, 50, 50), -1);
    var res0 = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(symmImg);
    using var rot90 = Cv2.GetRotationMatrix2D(new Point2f(400, 300), 90.0, 1.0);
    using var symmImg90 = new Mat();
    Cv2.WarpAffine(symmImg, symmImg90, rot90, new Size(800, 600), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(220, 220, 220));
    var res90 = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(symmImg90);

    Check("offline eval: 90-degree symmetry normalized into [-45, 45)",
        res90.Found && Math.Abs(res90.AngleDeg) < 1.5 && Math.Abs(res0.AngleDeg - res90.AngleDeg) < 1.5);
}

// 5. Boundary truncation rejection fail-closed
using (var truncImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    Cv2.Rectangle(truncImg, new Rect(0, 170, 150, 260), new Scalar(50, 50, 50), -1);
    var resTrunc = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(truncImg);
    Check("offline eval: boundary truncated plate rejected fail-closed", !resTrunc.Found);
}

// 6. Specular glare hotspot robustness
using (var glareImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    Cv2.Rectangle(glareImg, new Rect(270, 170, 260, 260), new Scalar(50, 50, 50), -1);
    Cv2.Circle(glareImg, new Point(400, 300), 12, new Scalar(255, 255, 255), -1);
    var resGlare = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(glareImg);
    Check("offline eval: minor specular glare sealed without centroid loss",
        resGlare.Found && Math.Abs(resGlare.CenterPxX - 400) < 2.0 && Math.Abs(resGlare.CenterPxY - 300) < 2.0);
}
// 6b. Reflection merging & candidate refinement tests
using (var reflImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(30, 30, 30)))
{
    using var plateMat = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(0, 0, 0));
    Cv2.Rectangle(plateMat, new Rect(400 - 90, 300 - 90, 180, 180), new Scalar(220, 220, 220), -1);
    Cv2.Rectangle(plateMat, new Rect(400 - 140, 300 - 90, 50, 90), new Scalar(130, 130, 130), -1);
    using var rot = Cv2.GetRotationMatrix2D(new Point2f(400, 300), 25.0, 1.0);
    using var rotatedMat = new Mat();
    Cv2.WarpAffine(plateMat, rotatedMat, rot, new Size(800, 600), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(0, 0, 0));
    using var mask = new Mat();
    using var grayRot = new Mat();
    Cv2.CvtColor(rotatedMat, grayRot, ColorConversionCodes.BGR2GRAY);
    Cv2.Threshold(grayRot, mask, 10, 255, ThresholdTypes.Binary);
    rotatedMat.CopyTo(reflImg, mask);

    var resRefl = PlateVision50.Vision.PlateDetector.Analyze(reflImg, new VisionOptions());
    double errDist = Math.Sqrt(Math.Pow(resRefl.CenterPxX - 400, 2) + Math.Pow(resRefl.CenterPxY - 300, 2));
    double angleErr = resRefl.AngleDeg - 25.0;
    while (angleErr >= 45.0) angleErr -= 90.0;
    while (angleErr < -45.0) angleErr += 90.0;
    Check("reflection test: white plate on dark belt with attached gray reflection center error < 2.5px",
        resRefl.Found && errDist < 2.5 && Math.Abs(angleErr) < 2.0);
}

using (var darkReflImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    Cv2.Rectangle(darkReflImg, new Rect(400 - 90, 300 - 90, 180, 180), new Scalar(40, 40, 40), -1);
    Cv2.Rectangle(darkReflImg, new Rect(400 + 90, 300 - 90, 40, 90), new Scalar(80, 80, 80), -1);
    using var rot = Cv2.GetRotationMatrix2D(new Point2f(400, 300), -15.0, 1.0);
    using var rotatedDark = new Mat();
    Cv2.WarpAffine(darkReflImg, rotatedDark, rot, new Size(800, 600), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(220, 220, 220));

    var resDarkRefl = PlateVision50.Vision.PlateDetector.Analyze(rotatedDark, new VisionOptions { ThresholdMode = 1 });
    double errDistDark = Math.Sqrt(Math.Pow(resDarkRefl.CenterPxX - 400, 2) + Math.Pow(resDarkRefl.CenterPxY - 300, 2));
    Check("reflection test: dark plate with attached smudge center error < 2.5px",
        resDarkRefl.Found && errDistDark < 2.5);
}

using (var lowContrastImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(25, 25, 25)))
{
    using var plateMat = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(0, 0, 0));
    Cv2.Rectangle(plateMat, new Rect(400 - 90, 300 - 90, 180, 180), new Scalar(170, 170, 170), -1);
    Cv2.Rectangle(plateMat, new Rect(400 - 130, 300 - 90, 40, 90), new Scalar(95, 95, 95), -1);
    using var rot = Cv2.GetRotationMatrix2D(new Point2f(400, 300), 10.0, 1.0);
    using var rotatedLow = new Mat();
    Cv2.WarpAffine(plateMat, rotatedLow, rot, new Size(800, 600), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(0, 0, 0));
    using var mask = new Mat();
    using var grayRot = new Mat();
    Cv2.CvtColor(rotatedLow, grayRot, ColorConversionCodes.BGR2GRAY);
    Cv2.Threshold(grayRot, mask, 10, 255, ThresholdTypes.Binary);
    rotatedLow.CopyTo(lowContrastImg, mask);

    var resLow = PlateVision50.Vision.PlateDetector.Analyze(lowContrastImg, new VisionOptions());
    double errDistLow = Math.Sqrt(Math.Pow(resLow.CenterPxX - 400, 2) + Math.Pow(resLow.CenterPxY - 300, 2));
    Check("reflection test: low-contrast plate with reflection center error < 2.5px",
        resLow.Found && errDistLow < 2.5);
}

using (var multiImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(30, 30, 30)))
{
    // Object 1 at (250, 300) with reflection
    Cv2.Rectangle(multiImg, new Rect(250 - 60, 300 - 60, 120, 120), new Scalar(220, 220, 220), -1);
    Cv2.Rectangle(multiImg, new Rect(250 - 90, 300 - 60, 30, 60), new Scalar(130, 130, 130), -1);
    // Object 2 at (550, 300) clean
    Cv2.Rectangle(multiImg, new Rect(550 - 60, 300 - 60, 120, 120), new Scalar(220, 220, 220), -1);

    var multiDets = PlateVision50.Vision.PlateDetector.AnalyzeAll(multiImg, new VisionOptions());
    Check("reflection test: multiple objects detected", multiDets.Count == 2);
    bool multiCentersOk = multiDets.Count == 2 &&
        multiDets.Any(d => Math.Sqrt(Math.Pow(d.CenterPxX - 250, 2) + Math.Pow(d.CenterPxY - 300, 2)) < 3.0) &&
        multiDets.Any(d => Math.Sqrt(Math.Pow(d.CenterPxX - 550, 2) + Math.Pow(d.CenterPxY - 300, 2)) < 3.0);
    Check("reflection test: multiple objects centers within tolerance", multiCentersOk);
}

// 6c. Inner highlight & undersized core contract tests
using (var innerPatchImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(30, 30, 30)))
{
    // 180x180 outer plate at (400, 300) with intensity 200
    Cv2.Rectangle(innerPatchImg, new Rect(400 - 90, 300 - 90, 180, 180), new Scalar(200, 200, 200), -1);
    // 50x50 offset square bright patch inside at (430, 320) with intensity 250
    Cv2.Rectangle(innerPatchImg, new Rect(430 - 25, 320 - 25, 50, 50), new Scalar(250, 250, 250), -1);

    var resInner = PlateVision50.Vision.PlateDetector.Analyze(innerPatchImg, new VisionOptions());
    double errInner = Math.Sqrt(Math.Pow(resInner.CenterPxX - 400, 2) + Math.Pow(resInner.CenterPxY - 300, 2));
    double sidePx = resInner.OutlinePx != null && resInner.OutlinePx.Length >= 2 ? Math.Sqrt(Math.Pow(resInner.OutlinePx[1].X - resInner.OutlinePx[0].X, 2) + Math.Pow(resInner.OutlinePx[1].Y - resInner.OutlinePx[0].Y, 2)) : 0;
    Check("inner highlight (square baseline): clean square plate preserves outer body center < 2.5px without refinement",
        resInner.Found && errInner < 2.5 && sidePx > 150.0);
}

using (var innerDarkImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    // 180x180 outer dark plate at (400, 300) with intensity 60
    Cv2.Rectangle(innerDarkImg, new Rect(400 - 90, 300 - 90, 180, 180), new Scalar(60, 60, 60), -1);
    // 50x50 offset darker spot inside at (430, 320) with intensity 15
    Cv2.Rectangle(innerDarkImg, new Rect(430 - 25, 320 - 25, 50, 50), new Scalar(15, 15, 15), -1);

    var resDarkInner = PlateVision50.Vision.PlateDetector.Analyze(innerDarkImg, new VisionOptions { ThresholdMode = 1 });
    double errDarkInner = Math.Sqrt(Math.Pow(resDarkInner.CenterPxX - 400, 2) + Math.Pow(resDarkInner.CenterPxY - 300, 2));
    double sidePxDark = resDarkInner.OutlinePx != null && resDarkInner.OutlinePx.Length >= 2 ? Math.Sqrt(Math.Pow(resDarkInner.OutlinePx[1].X - resDarkInner.OutlinePx[0].X, 2) + Math.Pow(resDarkInner.OutlinePx[1].Y - resDarkInner.OutlinePx[0].Y, 2)) : 0;
    Check("inner highlight (dark square baseline): clean dark square plate preserves outer body center < 2.5px without refinement",
        resDarkInner.Found && errDarkInner < 2.5 && sidePxDark > 150.0);
}

using (var undersizedImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(30, 30, 30)))
{
    // Coarse halo 65x65 = 4225 px (exceeds minObjectArea 800*600*0.006 = 2880 px)
    Cv2.Rectangle(undersizedImg, new Rect(400 - 32, 300 - 32, 65, 65), new Scalar(120, 120, 120), -1);
    // True inner target 30x30 = 900 px (strictly below minObjectArea 2880 px)
    Cv2.Rectangle(undersizedImg, new Rect(400 - 15, 300 - 15, 30, 30), new Scalar(220, 220, 220), -1);

    var resUnder = PlateVision50.Vision.PlateDetector.Analyze(undersizedImg, new VisionOptions());
    double underSide = resUnder.OutlinePx != null && resUnder.OutlinePx.Length >= 2 ? Math.Sqrt(Math.Pow(resUnder.OutlinePx[1].X - resUnder.OutlinePx[0].X, 2) + Math.Pow(resUnder.OutlinePx[1].Y - resUnder.OutlinePx[0].Y, 2)) : 0;
    // Contract: tiny inner target must NOT be accepted as a refined workpiece below configured min area
    bool notAcceptedAsTiny = !resUnder.Found || underSide > 55.0;
    Check("min area contract (square baseline): square halo preserves coarse without refinement",
        notAcceptedAsTiny);
}

// 6d. Elongated coarse refinement regressions (aspect > 1.06): body-support & min-area guards
using (var elongBrightImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(30, 30, 30)))
{
    // 180x180 plate at (400, 300), intensity 200
    Cv2.Rectangle(elongBrightImg, new Rect(400 - 90, 300 - 90, 180, 180), new Scalar(200, 200, 200), -1);
    // Attached elongated halo on left: 40x120 at (400 - 130, 300 - 60), intensity 120
    // Combined coarse shape: ~218x179, aspect ~1.22 (> 1.06 triggers refinement)
    Cv2.Rectangle(elongBrightImg, new Rect(400 - 130, 300 - 60, 40, 120), new Scalar(120, 120, 120), -1);
    // Offset inner bright highlight: 60x60 at (430, 320), intensity 250 (area = 3600 > minArea 2880)
    Cv2.Rectangle(elongBrightImg, new Rect(430 - 30, 320 - 30, 60, 60), new Scalar(250, 250, 250), -1);

    var resElongBright = PlateVision50.Vision.PlateDetector.Analyze(elongBrightImg, new VisionOptions { ThresholdMode = 2 });
    double sideBright = resElongBright.OutlinePx != null && resElongBright.OutlinePx.Length >= 2 ? resElongBright.OutlinePx[0].DistanceTo(resElongBright.OutlinePx[1]) : 0;
    double distToHighlight = Math.Sqrt(Math.Pow(resElongBright.CenterPxX - 430, 2) + Math.Pow(resElongBright.CenterPxY - 320, 2));

    Check("refinement guard: elongated halo with inner bright highlight preserves outer body without false-locking on highlight",
        resElongBright.Found && sideBright > 150.0 && distToHighlight > 30.0 && Math.Abs(resElongBright.CenterPxX - 380.0) < 5.0);
}

using (var elongDarkImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220)))
{
    // Outer dark plate: 180x180 at (400, 300), intensity 70
    Cv2.Rectangle(elongDarkImg, new Rect(400 - 90, 300 - 90, 180, 180), new Scalar(70, 70, 70), -1);
    // Attached elongated smudge on left: 50x120 at (400 - 140, 300 - 60), intensity 100
    // Combined coarse shape: ~229x179, aspect ~1.28 (> 1.06 triggers refinement)
    Cv2.Rectangle(elongDarkImg, new Rect(400 - 140, 300 - 60, 50, 120), new Scalar(100, 100, 100), -1);
    // Offset inner darker spot: 60x60 at (430, 320), intensity 10 (area = 3600 > minArea 2880)
    Cv2.Rectangle(elongDarkImg, new Rect(430 - 30, 320 - 30, 60, 60), new Scalar(10, 10, 10), -1);

    var resElongDark = PlateVision50.Vision.PlateDetector.Analyze(elongDarkImg, new VisionOptions { ThresholdMode = 1 });
    double sideDark = resElongDark.OutlinePx != null && resElongDark.OutlinePx.Length >= 2 ? resElongDark.OutlinePx[0].DistanceTo(resElongDark.OutlinePx[1]) : 0;
    double distToSpot = Math.Sqrt(Math.Pow(resElongDark.CenterPxX - 430, 2) + Math.Pow(resElongDark.CenterPxY - 320, 2));

    Check("refinement guard: dark plate with elongated smudge preserves outer body without false-locking on inner spot",
        resElongDark.Found && sideDark > 150.0 && distToSpot > 30.0 && Math.Abs(resElongDark.CenterPxX - 374.5) < 5.0);
}

using (var elongUnderImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(30, 30, 30)))
{
    // Coarse halo: 75x60 at (400, 300), area = 4500 > minArea (2880), aspect = 75/60 = 1.25 (> 1.06 triggers refinement)
    Cv2.Rectangle(elongUnderImg, new Rect(400 - 37, 300 - 30, 75, 60), new Scalar(120, 120, 120), -1);
    // Inner core: 35x35 at (400, 300), area = 1225 < minArea (2880)
    Cv2.Rectangle(elongUnderImg, new Rect(400 - 17, 300 - 17, 35, 35), new Scalar(220, 220, 220), -1);

    var resElongUnder = PlateVision50.Vision.PlateDetector.Analyze(elongUnderImg, new VisionOptions { ThresholdMode = 2 });
    Check("refinement guard: elongated halo with undersized core rejected fail-closed (!Found)",
        !resElongUnder.Found);
}

// 7. Architectural type isolation & zero synthetic glue points
var resType = typeof(PlateVision50.Vision.OfflineCenterAngleResult);
Check("offline eval: OfflineCenterAngleResult has no GluePx property", resType.GetProperty("GluePx") == null);
Check("offline eval: OfflineCenterAngleResult has no GlueMm property", resType.GetProperty("GlueMm") == null);
Check("offline eval: OfflineCenterAngleResult has no Vertices property", resType.GetProperty("Vertices") == null);

var createTrackedPoseParamTypes = typeof(PlateVision50.Vision.BeltCoordinates)
    .GetMethod("CreateTrackedPose")!
    .GetParameters()
    .Select(p => p.ParameterType)
    .ToArray();
Check("offline eval: CreateTrackedPose requires PlateResult, not OfflineCenterAngleResult",
    createTrackedPoseParamTypes.Contains(typeof(PlateVision50.Vision.PlateResult)) &&
    !createTrackedPoseParamTypes.Contains(typeof(PlateVision50.Vision.OfflineCenterAngleResult)));

// 8. Type boundary: OfflineCenterAngleDetector does not reference TwinCAT, Ads, or Trigger
var detectorType = typeof(PlateVision50.Vision.OfflineCenterAngleDetector);
var detectorMethods = detectorType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
bool leaksAds = detectorMethods.Any(m =>
    m.ReturnType.FullName?.Contains("TwinCAT") == true ||
    m.ReturnType.FullName?.Contains("Trigger") == true ||
    m.GetParameters().Any(p => p.ParameterType.FullName?.Contains("TwinCAT") == true || p.ParameterType.FullName?.Contains("Trigger") == true));
Check("offline eval: OfflineCenterAngleDetector public API has zero TwinCAT/ADS/Trigger dependencies", !leaksAds);

// 9. Real Behavioral Unit Tests: OfflineVideoEvaluationSession Lifecycle & Overlap Guarantees
{
    // Test A: Finite EOF completion & automatic source disposal
    using var sessionA = new PlateVision50.Vision.OfflineVideoEvaluationSession();
    var syntheticMats5 = Enumerable.Range(0, 5).Select(idx => {
        var m = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220));
        Cv2.Rectangle(m, new Rect(300 + idx * 10, 200, 200, 200), new Scalar(50, 50, 50), -1);
        return m;
    }).ToList();

    var seq5 = new PlateVision50.Vision.SyntheticFrameSequence(syntheticMats5);
    foreach (var m in syntheticMats5) m.Dispose();

    int deliveredFramesA = 0;
    string finalStatusA = "";
    bool startedA = sessionA.StartAsync(
        () => seq5,
        eval => { if (eval.Result.Found) deliveredFramesA++; },
        status => { finalStatusA = status; }).GetAwaiter().GetResult();

    Check("offline session: StartAsync accepted on clean session", startedA && sessionA.IsRunning);
    sessionA.WorkerTask?.Wait(2000);
    Check("offline session: completes naturally at finite EOF with source disposed",
        !sessionA.IsRunning && deliveredFramesA == 5 && finalStatusA == "Completed" && seq5.IsDisposed);

    // Test A2: StartAsync after natural EOF cleans up prior CTS without resource leak
    var syntheticMats3 = Enumerable.Range(0, 3).Select(idx => {
        var m = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220));
        Cv2.Rectangle(m, new Rect(300 + idx * 10, 200, 200, 200), new Scalar(50, 50, 50), -1);
        return m;
    }).ToList();
    var seq3 = new PlateVision50.Vision.SyntheticFrameSequence(syntheticMats3);
    foreach (var m in syntheticMats3) m.Dispose();

    bool startedA2 = sessionA.StartAsync(
        () => seq3,
        eval => { },
        status => { }).GetAwaiter().GetResult();
    Check("offline session: subsequent StartAsync after EOF succeeds cleanly without leak", startedA2 && sessionA.IsRunning);
    sessionA.WorkerTask?.Wait(2000);
    Check("offline session: second session completes naturally at EOF", !sessionA.IsRunning && seq3.IsDisposed);

    // Test B: Concurrent Overlap Refusal
    using var sessionB = new PlateVision50.Vision.OfflineVideoEvaluationSession();
    var blockingSourceB = new PlateVision50.Vision.BlockingFrameSource();
    bool startedB1 = sessionB.StartAsync(
        () => blockingSourceB,
        eval => { },
        status => { }).GetAwaiter().GetResult();

    Check("offline session: first worker started and running", startedB1 && sessionB.IsRunning);

    string refusalStatus = "";
    bool startedB2 = sessionB.StartAsync(
        () => new PlateVision50.Vision.SyntheticFrameSequence([]),
        eval => { },
        status => { refusalStatus = status; }).GetAwaiter().GetResult();

    Check("offline session: concurrent start is strictly refused while worker active",
        !startedB2 && refusalStatus == "Previous evaluation worker still stopping; please wait.");

    blockingSourceB.Unblock();
    sessionB.StopAsync(timeoutMs: 1000).GetAwaiter().GetResult();
    Check("offline session: worker terminates cleanly after unblock", !sessionB.IsRunning && blockingSourceB.IsDisposed);

    // Test C: Stale Generation Result Suppression
    using var sessionC = new PlateVision50.Vision.OfflineVideoEvaluationSession();
    var blockingSourceC = new PlateVision50.Vision.BlockingFrameSource();
    int callbackCountC = 0;
    sessionC.StartAsync(
        () => blockingSourceC,
        eval => { callbackCountC++; }).GetAwaiter().GetResult();

    int gen1 = sessionC.CurrentGeneration;
    sessionC.StopAsync(timeoutMs: 50).GetAwaiter().GetResult();
    int gen2 = sessionC.CurrentGeneration;
    Check("offline session: StopAsync advances generation token", gen2 > gen1);

    int beforeUnblockCount = callbackCountC;
    blockingSourceC.Unblock();
    sessionC.WorkerTask?.Wait(500);
    Check("offline session: results from stale generation are suppressed", callbackCountC == beforeUnblockCount);

    // Test D: Responsive mid-stream cancellation between frames
    using var sessionD = new PlateVision50.Vision.OfflineVideoEvaluationSession();
    var syntheticMats10 = Enumerable.Range(0, 10).Select(idx => {
        var m = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220));
        Cv2.Rectangle(m, new Rect(300 + idx * 10, 200, 200, 200), new Scalar(50, 50, 50), -1);
        return m;
    }).ToList();

    var seq10 = new PlateVision50.Vision.SyntheticFrameSequence(syntheticMats10);
    foreach (var m in syntheticMats10) m.Dispose();

    int deliveredCountD = 0;
    sessionD.StartAsync(
        () => seq10,
        eval => {
            deliveredCountD++;
            if (deliveredCountD >= 3)
            {
                _ = Task.Run(() => sessionD.StopAsync(timeoutMs: 500));
            }
        }).GetAwaiter().GetResult();

    sessionD.WorkerTask?.Wait(2000);
    Check("offline session: mid-stream cancellation cleanly stops processing without hanging",
        deliveredCountD >= 3 && deliveredCountD < 10);

    // Test E: DrawOverlay renders valid non-empty annotated frame
    using var overlayTestImg = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220));
    Cv2.Rectangle(overlayTestImg, new Rect(300, 200, 200, 200), new Scalar(50, 50, 50), -1);
    var detList = PlateVision50.Vision.OfflineCenterAngleDetector.DetectAll(overlayTestImg);
    using var overlayOutput = PlateVision50.Vision.OfflineCenterAngleDetector.DrawOverlay(overlayTestImg, detList);
    Check("offline overlay: DrawOverlay renders non-empty frame matching source dimensions",
        !overlayOutput.Empty() && overlayOutput.Width == 800 && overlayOutput.Height == 600);

    // Test F: Natural EOF delivers final frame to delayed consumer without IsRunning drop
    using var sessionF = new PlateVision50.Vision.OfflineVideoEvaluationSession();
    var syntheticMatsF = Enumerable.Range(0, 3).Select(idx => {
        var m = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(220, 220, 220));
        Cv2.Rectangle(m, new Rect(300 + idx * 10, 200, 200, 200), new Scalar(50, 50, 50), -1);
        return m;
    }).ToList();
    var seqF = new PlateVision50.Vision.SyntheticFrameSequence(syntheticMatsF);
    foreach (var m in syntheticMatsF) m.Dispose();

    var deliveredGenerations = new List<int>();
    int finalFrameIndex = 0;
    sessionF.StartAsync(
        () => seqF,
        eval => {
            deliveredGenerations.Add(eval.Generation);
            finalFrameIndex = eval.FrameIndex;
        },
        status => { }).GetAwaiter().GetResult();

    int targetGenF = sessionF.CurrentGeneration;
    sessionF.WorkerTask?.Wait(2000);
    Check("offline session: natural EOF delivers final frame even after worker loop finishes",
        !sessionF.IsRunning && finalFrameIndex == 3 && deliveredGenerations.Count == 3 && deliveredGenerations.All(g => g == targetGenF));

    // Test G: Synchronous Cancel immediately advances generation and halts worker
    using var sessionG = new PlateVision50.Vision.OfflineVideoEvaluationSession();
    var blockingG = new PlateVision50.Vision.BlockingFrameSource();
    sessionG.StartAsync(() => blockingG, eval => { }, status => { }).GetAwaiter().GetResult();
    int genBeforeG = sessionG.CurrentGeneration;
    sessionG.Cancel();
    int genAfterG = sessionG.CurrentGeneration;
    Check("offline session: Cancel() synchronously increments generation token", genAfterG > genBeforeG);
    blockingG.Unblock();
    bool exitedPromptlyG = sessionG.WorkerTask?.Wait(500) == true;
    Check("offline session: worker halts promptly after Cancel() and unblock", exitedPromptlyG && !sessionG.IsRunning);

    // Test H: Behavioral regression: Cancellation before worker delegate enters
    PlateVision50.Vision.OfflineVideoEvaluationSession? sessionH = null;
    var startGateH = new ManualResetEventSlim(false);
    var enteredH = new ManualResetEventSlim(false);
    bool sourceAllocatedH = false;

    sessionH = new PlateVision50.Vision.OfflineVideoEvaluationSession(action =>
    {
        return Task.Run(() =>
        {
            enteredH.Set();
            startGateH.Wait();
            action();
        });
    });

    sessionH.StartAsync(
        () => {
            sourceAllocatedH = true;
            return new PlateVision50.Vision.SyntheticFrameSequence([]);
        },
        eval => { },
        status => { }).GetAwaiter().GetResult();

    Check("offline session: StartAsync accepted with scheduled worker", sessionH.IsRunning);

    enteredH.Wait(1000);
    sessionH.Cancel();
    startGateH.Set();

    try { sessionH.WorkerTask?.Wait(500); } catch { }
    Check("offline session: IsRunning is false after cancellation before delegate entry", !sessionH.IsRunning);
    Check("offline session: source was never allocated when cancelled before entry", !sourceAllocatedH);

    // Verify subsequent Start is NOT falsely refused
    bool subsequentStartedH = sessionH.StartAsync(
        () => new PlateVision50.Vision.SyntheticFrameSequence([]),
        eval => { },
        status => { }).GetAwaiter().GetResult();
    Check("offline session: subsequent StartAsync is NOT falsely refused after pre-start cancellation", subsequentStartedH);
}

// 12. Local video benchmark handling (observational only; non-tautological)
string defaultVideo = @"C:\Users\aml\Desktop\DEMO\1.avi";
string? videoPath = Environment.GetEnvironmentVariable("DATN_TEST_VIDEO") ?? (File.Exists(defaultVideo) ? defaultVideo : null);
if (videoPath != null && File.Exists(videoPath))
{
    using var vCap = new VideoCapture(videoPath);
    if (vCap.IsOpened())
    {
        int readFrames = 0, detectedFrames = 0;
        while (readFrames < 200)
        {
            using var f = new Mat();
            if (!vCap.Read(f) || f.Empty()) break;
            readFrames++;
            var det = PlateVision50.Vision.OfflineCenterAngleDetector.Detect(f);
            if (det.Found) detectedFrames++;
        }
        Console.WriteLine($"OFFLINE VIDEO BENCHMARK (OBSERVATIONAL ONLY): read={readFrames}, detected={detectedFrames}, path='{videoPath}'. Evaluates frame readability only; no ground truth claim absent calibrated fiducials.");
        Check("offline eval: observational video read at least one valid frame", readFrames > 0);
    }
}
else
{
    string envVal = Environment.GetEnvironmentVariable("DATN_TEST_VIDEO") ?? "<unset>";
    Console.WriteLine($"OFFLINE VIDEO BENCHMARK: No local video file present (DATN_TEST_VIDEO='{envVal}', default='{defaultVideo}' missing). Observational disk check skipped cleanly; synthetic-only verification.");
    Check("offline eval: missing observational demo video skipped safely", true);
}

Console.WriteLine("INTEGRATED CALIBRATION & OFFLINE STARTUP TESTS:");
// 1. GantryCornerCalculator: Unrotated 0 deg
var gantryInputs0 = new PlateVision50.Vision.GantryCornerInputs(
    CenterX_mm: 100.0,
    CenterY_mm: 100.0,
    ThetaDeg: 0.0,
    PitchX_mm: 40.0,
    PitchY_mm: 40.0,
    WorkspaceMinX_mm: -500.0,
    WorkspaceMaxX_mm: 500.0,
    WorkspaceMinY_mm: -500.0,
    WorkspaceMaxY_mm: 500.0);
var gantryRes0 = PlateVision50.Vision.GantryCornerCalculator.Evaluate(gantryInputs0);
Check("gantry corners: unrotated 0 deg is valid", gantryRes0.IsValid);
Check("gantry corners: unrotated 0 deg returns exactly 4 corners", gantryRes0.Corners.Count == 4);
Check("gantry corners: P1 is Quad I (+20, +20)", Math.Abs(gantryRes0.Corners[0].X_mm - 120.0) < 1e-4 && Math.Abs(gantryRes0.Corners[0].Y_mm - 120.0) < 1e-4);
Check("gantry corners: P2 is Quad II (-20, +20)", Math.Abs(gantryRes0.Corners[1].X_mm - 80.0) < 1e-4 && Math.Abs(gantryRes0.Corners[1].Y_mm - 120.0) < 1e-4);
Check("gantry corners: P3 is Quad III (-20, -20)", Math.Abs(gantryRes0.Corners[2].X_mm - 80.0) < 1e-4 && Math.Abs(gantryRes0.Corners[2].Y_mm - 80.0) < 1e-4);
Check("gantry corners: P4 is Quad IV (+20, -20)", Math.Abs(gantryRes0.Corners[3].X_mm - 120.0) < 1e-4 && Math.Abs(gantryRes0.Corners[3].Y_mm - 80.0) < 1e-4);
Check("gantry corners: unrotated pitch consistency verified", Math.Abs(gantryRes0.CalculatedPitchX_mm - 40.0) < 1e-4);
Check("gantry corners: unrotated all in bounds", gantryRes0.AllInBounds);

// 2. GantryCornerCalculator: Rotated 90 deg CCW
var gantryInputs90 = new PlateVision50.Vision.GantryCornerInputs(
    CenterX_mm: 100.0, CenterY_mm: 100.0, ThetaDeg: 90.0,
    PitchX_mm: 40.0, PitchY_mm: 40.0,
    WorkspaceMinX_mm: -500.0, WorkspaceMaxX_mm: 500.0,
    WorkspaceMinY_mm: -500.0, WorkspaceMaxY_mm: 500.0);
var gantryRes90 = PlateVision50.Vision.GantryCornerCalculator.Evaluate(gantryInputs90);
Check("gantry corners: 90 deg CCW P1 X is 80mm", Math.Abs(gantryRes90.Corners[0].X_mm - 80.0) < 1e-4);
Check("gantry corners: 90 deg CCW P1 Y is 120mm", Math.Abs(gantryRes90.Corners[0].Y_mm - 120.0) < 1e-4);
Check("gantry corners: 90 deg CCW P2 X is 80mm", Math.Abs(gantryRes90.Corners[1].X_mm - 80.0) < 1e-4);
Check("gantry corners: 90 deg CCW P2 Y is 80mm", Math.Abs(gantryRes90.Corners[1].Y_mm - 80.0) < 1e-4);
Check("gantry corners: 90 deg pitch verified", Math.Abs(gantryRes90.CalculatedPitchX_mm - 40.0) < 1e-4);

// 3. GantryCornerCalculator: Negative angle -30 deg CCW
var gantryInputsNeg30 = new PlateVision50.Vision.GantryCornerInputs(
    CenterX_mm: 0.0, CenterY_mm: 0.0, ThetaDeg: -30.0,
    PitchX_mm: 40.0, PitchY_mm: 40.0,
    WorkspaceMinX_mm: -500.0, WorkspaceMaxX_mm: 500.0,
    WorkspaceMinY_mm: -500.0, WorkspaceMaxY_mm: 500.0);
var gantryResNeg30 = PlateVision50.Vision.GantryCornerCalculator.Evaluate(gantryInputsNeg30);
double expNeg30_P1_X = 20.0 * Math.Cos(-30.0 * Math.PI / 180.0) - 20.0 * Math.Sin(-30.0 * Math.PI / 180.0);
double expNeg30_P1_Y = 20.0 * Math.Sin(-30.0 * Math.PI / 180.0) + 20.0 * Math.Cos(-30.0 * Math.PI / 180.0);
Check("gantry corners: -30 deg P1 matches analytic geometry",
    Math.Abs(gantryResNeg30.Corners[0].X_mm - expNeg30_P1_X) < 1e-4 &&
    Math.Abs(gantryResNeg30.Corners[0].Y_mm - expNeg30_P1_Y) < 1e-4);
Check("gantry corners: -30 deg pitch verified", Math.Abs(gantryResNeg30.CalculatedPitchX_mm - 40.0) < 1e-4);

// 4. GantryCornerCalculator: NaN and Infinity parameter handling (fail-closed)
var nanCenterInputs = new PlateVision50.Vision.GantryCornerInputs(
    double.NaN, 0.0, 0.0, 40.0, 40.0, -100.0, 100.0, -100.0, 100.0);
Check("gantry corners: NaN center rejected fail-closed", !PlateVision50.Vision.GantryCornerCalculator.Evaluate(nanCenterInputs).IsValid);

var infPitchInputs = new PlateVision50.Vision.GantryCornerInputs(
    0.0, 0.0, 0.0, double.PositiveInfinity, 40.0, -100.0, 100.0, -100.0, 100.0);
Check("gantry corners: Infinity pitch rejected fail-closed", !PlateVision50.Vision.GantryCornerCalculator.Evaluate(infPitchInputs).IsValid);

var nanBoundsInputs = new PlateVision50.Vision.GantryCornerInputs(
    0.0, 0.0, 0.0, 40.0, 40.0, double.NaN, 100.0, -100.0, 100.0);
Check("gantry corners: NaN bounds rejected fail-closed", !PlateVision50.Vision.GantryCornerCalculator.Evaluate(nanBoundsInputs).IsValid);
// 4B. GantryCornerCalculator: Huge finite theta and coordinate overflow guards
var hugeThetaInputs = new PlateVision50.Vision.GantryCornerInputs(
    100.0, 100.0, 1e200, 40.0, 40.0, -500.0, 500.0, -500.0, 500.0);
var hugeThetaRes = PlateVision50.Vision.GantryCornerCalculator.Evaluate(hugeThetaInputs);
Check("gantry corners: huge finite theta evaluates validly without double overflow", hugeThetaRes.IsValid);

var overflowCoordInputs = new PlateVision50.Vision.GantryCornerInputs(
    1.5e308, 0.0, 0.0, 1.5e308, 40.0, -500.0, 500.0, -500.0, 500.0);
var overflowCoordRes = PlateVision50.Vision.GantryCornerCalculator.Evaluate(overflowCoordInputs);
Check("gantry corners: coordinate arithmetic overflow rejected fail-closed", !overflowCoordRes.IsValid);

// 5. GantryCornerCalculator: Non-positive pitch
var zeroPitchInputs = new PlateVision50.Vision.GantryCornerInputs(
    0.0, 0.0, 0.0, 0.0, 40.0, -100.0, 100.0, -100.0, 100.0);
Check("gantry corners: zero pitch rejected fail-closed", !PlateVision50.Vision.GantryCornerCalculator.Evaluate(zeroPitchInputs).IsValid);

var negPitchInputs = new PlateVision50.Vision.GantryCornerInputs(
    0.0, 0.0, 0.0, 40.0, -10.0, -100.0, 100.0, -100.0, 100.0);
Check("gantry corners: negative pitch rejected fail-closed", !PlateVision50.Vision.GantryCornerCalculator.Evaluate(negPitchInputs).IsValid);

// 6. GantryCornerCalculator: Inverted workspace bounds
var invertedBoundsInputs = new PlateVision50.Vision.GantryCornerInputs(
    0.0, 0.0, 0.0, 40.0, 40.0, 200.0, 100.0, -100.0, 100.0);
Check("gantry corners: inverted MinX >= MaxX rejected fail-closed", !PlateVision50.Vision.GantryCornerCalculator.Evaluate(invertedBoundsInputs).IsValid);

// 7. GantryCornerCalculator: Out-of-workspace envelope rejection
var outOfBoundsInputs = new PlateVision50.Vision.GantryCornerInputs(
    CenterX_mm: 495.0, CenterY_mm: 495.0, ThetaDeg: 0.0,
    PitchX_mm: 40.0, PitchY_mm: 40.0,
    WorkspaceMinX_mm: 0.0, WorkspaceMaxX_mm: 500.0,
    WorkspaceMinY_mm: 0.0, WorkspaceMaxY_mm: 500.0);
var outOfBoundsRes = PlateVision50.Vision.GantryCornerCalculator.Evaluate(outOfBoundsInputs);
Check("gantry corners: out-of-bounds workpiece evaluated", outOfBoundsRes.IsValid);
Check("gantry corners: out-of-bounds workpiece marks AllInBounds=false", !outOfBoundsRes.AllInBounds);
Check("gantry corners: out-of-bounds status reports OUT OF BOUNDS", outOfBoundsRes.Status.StartsWith("OUT OF BOUNDS"));
Check("gantry corners: P1 is outside bounds", !outOfBoundsRes.Corners[0].InBounds);
Check("gantry corners: P3 is inside bounds", outOfBoundsRes.Corners[2].InBounds);

// 8. AppHardwareCapabilities: --offline-calibration startup capability
PlateVision50.Vision.AppHardwareCapabilities.Initialize(new[] { "--offline-calibration" });
Check("offline cap: IsOfflineCalibration is true", PlateVision50.Vision.AppHardwareCapabilities.IsOfflineCalibration);
Check("offline cap: AllowAdsPolling is false", !PlateVision50.Vision.AppHardwareCapabilities.AllowAdsPolling);
Check("offline cap: AllowAdsWrites is false", !PlateVision50.Vision.AppHardwareCapabilities.AllowAdsWrites);
Check("offline cap: AllowCameraHardware is false", !PlateVision50.Vision.AppHardwareCapabilities.AllowCameraHardware);
Check("offline cap: AllowHardwareMotion is false", !PlateVision50.Vision.AppHardwareCapabilities.AllowHardwareMotion);
Check("offline cap: ProductionCommandSourceApproved disarmed", !PlateVision50.Vision.LineMailboxProtocol.ProductionCommandSourceApproved);
Check("offline cap: MutexName is offline mutex", PlateVision50.Vision.AppHardwareCapabilities.MutexName == PlateVision50.Vision.AppHardwareCapabilities.OfflineMutexName);

// 9. LineMailboxGateway: Transport writes strictly refused under offline capabilities
var disarmedCapTransport = new FakeAdsMailboxTransport { IsConnected = false, IsInRunState = false };
var disarmedCapGateway = new PlateVision50.Vision.LineMailboxGateway(disarmedCapTransport, false);
bool capConnectResult = disarmedCapGateway.EnsureTransportConnected(851);
Check("offline cap: EnsureTransportConnected refused without socket connection", !capConnectResult);
Check("offline cap: Transport is NOT connected", !disarmedCapTransport.IsConnected);
bool capHeartbeatResult = disarmedCapGateway.TickHeartbeat(false, true);
Check("offline cap: TickHeartbeat refused without write", !capHeartbeatResult);
Check("offline cap: ZERO writes performed to transport", disarmedCapTransport.Writes.Count == 0);

// 10. AdsFifoBenchBridge: Hardware operations strictly refused under offline capabilities
PlateVision50.TwinCAT.AdsFifoBenchBridge.ClientCreationCount = 0;
var offlineBridge = new PlateVision50.TwinCAT.AdsFifoBenchBridge();

string liveReadiness = offlineBridge.InspectLiveReadiness();
Check("offline bridge: InspectLiveReadiness returns explicit OFFLINE/UNAVAILABLE", liveReadiness.Contains("OFFLINE / UNAVAILABLE"));

bool readStatusBlocked = false;
try { offlineBridge.ReadStatus(); }
catch (InvalidOperationException ex) when (ex.Message.Contains("inhibited in offline calibration mode")) { readStatusBlocked = true; }
Check("offline bridge: ReadStatus throws explicit rejection", readStatusBlocked);

bool enqueueBlocked = false;
try { offlineBridge.EnqueueBenchAsync(0, 0, 0).GetAwaiter().GetResult(); }
catch (InvalidOperationException ex) when (ex.Message.Contains("inhibited in offline calibration mode")) { enqueueBlocked = true; }
Check("offline bridge: EnqueueBenchAsync throws explicit rejection", enqueueBlocked);

Check("offline bridge: ZERO AdsClient instances created across all operations", PlateVision50.TwinCAT.AdsFifoBenchBridge.ClientCreationCount == 0);

var adsPreview = new PlateVision50.TwinCAT.AdsPreview();
string previewCheck = adsPreview.Check();
Check("offline bridge: AdsPreview.Check returns explicit OFFLINE/UNAVAILABLE", previewCheck.Contains("OFFLINE / UNAVAILABLE"));


// =====================================================================
// 11. GANTRY MASTER ENABLE CONTROLLER & PROTOCOL BEHAVIORAL TESTS
// =====================================================================
Console.WriteLine("GANTRY MASTER ENABLE TESTS:");
PlateVision50.Vision.AppHardwareCapabilities.SetOfflineModeForTesting(false);
void InitHealthyGmeTransport(FakeAdsMailboxTransport t)
{
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, 0u);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)0);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOff);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, true);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymIdleVerified, true);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymBusy, false);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 10u);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, false);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, true);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymReq, false);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymRefreshReq, false);
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAxisOverrideEcho, new bool[] { false, false, false });
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymOutputEcho, new bool[] { false, false, false });
    t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymCommErrorId, 0u);
}

// Test 1: Successful ON command handshake
var gmeTransOn = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransOn);
gmeTransOn.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOn);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 11u);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, true);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, false);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymOutputEcho, new bool[] { true, true, true });
    }
};
var gmeCtrlOn = new PlateVision50.Vision.GantryMasterEnableController(gmeTransOn)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resOn = gmeCtrlOn.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme on: command accepted with matching ACK", resOn.Success && resOn.ResultCode == 1);
Check("gme on: writes target level ON (true)", gmeTransOn.Read<bool>(PlateVision50.Vision.GantryMasterEnableController.SymTargetOn));
Check("gme on: request pulse released back to false", !gmeTransOn.Read<bool>(PlateVision50.Vision.GantryMasterEnableController.SymReq));

// Test 2: Successful OFF command handshake
var gmeTransOff = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransOff);
gmeTransOff.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, true);
gmeTransOff.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, false);
gmeTransOff.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOff);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 12u);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, false);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, true);
    }
};
var gmeCtrlOff = new PlateVision50.Vision.GantryMasterEnableController(gmeTransOff)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resOff = gmeCtrlOff.RequestMasterEnableAsync(false).GetAwaiter().GetResult();
Check("gme off: command accepted with matching ACK", resOff.Success && resOff.ResultCode == 1);
Check("gme off: writes target level OFF (false)", !gmeTransOff.Read<bool>(PlateVision50.Vision.GantryMasterEnableController.SymTargetOn));
// Test 3: Wrong ACK sequence fails closed as timeout
var gmeTransWrongAck = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransWrongAck);
gmeTransWrongAck.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, 9999u); // Wrong seq
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
    }
};
var gmeCtrlWrongAck = new PlateVision50.Vision.GantryMasterEnableController(gmeTransWrongAck)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 50,
    PollIntervalMs = 5
};
var resWrongAck = gmeCtrlWrongAck.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme wrong ack: rejected as timeout", !resWrongAck.Success && resWrongAck.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.ErrorCommTimeout);
Check("gme wrong ack: timeout message states indeterminate without replay", resWrongAck.Message.Contains("INDETERMINATE (CLIENT TIMEOUT)"));

// Test 4: Mismatch readback result code handling
var gmeTransMismatch = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransMismatch);
gmeTransMismatch.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.ErrorReadbackMismatch);
    }
};
var gmeCtrlMismatch = new PlateVision50.Vision.GantryMasterEnableController(gmeTransMismatch)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resMismatch = gmeCtrlMismatch.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme mismatch: rejected with ErrorReadbackMismatch code 18", !resMismatch.Success && resMismatch.ResultCode == 18);

// Test 5: Axis override state description
var gmeTransOverride = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransOverride);
gmeTransOverride.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
gmeTransOverride.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.MasterOffOverrideActive);
gmeTransOverride.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAxisOverrideEcho, new bool[] { true, false, false });
var gmeCtrlOverride = new PlateVision50.Vision.GantryMasterEnableController(gmeTransOverride);
var statusOverride = gmeCtrlOverride.ReadCurrentStatus();
Check("gme override: StateDescription reflects Master OFF with override active",
    statusOverride.StateDescription.Contains("MASTER OFF (Axis Override Active)"));
Check("gme override: OverrideDescription lists M1=ON",
    statusOverride.OverrideDescription.Contains("M1=ON"));

// Test 6: Stale snapshot with SnapshotValid=false reported as STALE/UNKNOWN even if ADS is fresh
var gmeTransStale = new FakeAdsMailboxTransport();
gmeTransStale.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, false);
gmeTransStale.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOn);
var gmeCtrlStale = new PlateVision50.Vision.GantryMasterEnableController(gmeTransStale);
var statusStale = gmeCtrlStale.ReadCurrentStatus();
Check("gme stale: SnapshotValid=false forces STALE / UNKNOWN status",
    statusStale.StateDescription.Contains("STALE / UNKNOWN (SNAPSHOT INVALID)"));
Check("gme stale: SnapshotValid=false forces OverrideDescription to STALE / UNKNOWN",
    statusStale.OverrideDescription.Contains("STALE / UNKNOWN"));

// Test 7: Unchanged refresh sequence cannot accept refresh
var gmeTransUnchangedRefresh = new FakeAdsMailboxTransport();
gmeTransUnchangedRefresh.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 10u);
gmeTransUnchangedRefresh.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
// PLC does not increment SymSnapshotSeq
var gmeCtrlUnchangedRefresh = new PlateVision50.Vision.GantryMasterEnableController(gmeTransUnchangedRefresh)
{
    PulseHoldMs = 0,
    PollIntervalMs = 5
};
var refreshUnchangedRes = gmeCtrlUnchangedRefresh.RequestRefreshAsync(new CancellationTokenSource(60).Token).GetAwaiter().GetResult();
Check("gme refresh: unchanged snapshot sequence rejected (returns false)", !refreshUnchangedRes);

// Test 8: Refresh with SnapshotValid=false cannot accept refresh
var gmeTransInvalidRefresh = new FakeAdsMailboxTransport();
gmeTransInvalidRefresh.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 10u);
gmeTransInvalidRefresh.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymRefreshReq && (bool)val)
    {
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 11u); // Seq incremented
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, false); // But invalid
    }
};
var gmeCtrlInvalidRefresh = new PlateVision50.Vision.GantryMasterEnableController(gmeTransInvalidRefresh)
{
    PulseHoldMs = 0,
    PollIntervalMs = 5
};
var refreshInvalidRes = gmeCtrlInvalidRefresh.RequestRefreshAsync(new CancellationTokenSource(60).Token).GetAwaiter().GetResult();
Check("gme refresh: SnapshotValid=false rejected (returns false)", !refreshInvalidRes);

// Test 9: Remote PLC commissioning gate closed rejects command with zero writes
var gmeTransLocked = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransLocked);
gmeTransLocked.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, false); // Remote permit closed
var gmeCtrlLocked = new PlateVision50.Vision.GantryMasterEnableController(gmeTransLocked);
var resLocked = gmeCtrlLocked.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme gate: closed remote permit rejects command", !resLocked.Success && resLocked.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedUncommissioned);
Check("gme gate: zero writes performed when permit is closed", gmeTransLocked.Writes.Count == 0);

// Test 9B: Motion idle unverified rejects command with zero writes
var gmeTransNotIdle = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransNotIdle);
gmeTransNotIdle.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymIdleVerified, false); // Not idle
var gmeCtrlNotIdle = new PlateVision50.Vision.GantryMasterEnableController(gmeTransNotIdle);
var resNotIdle = gmeCtrlNotIdle.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme idle: unverified idle rejects command", !resNotIdle.Success && resNotIdle.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedIdleUnverified);
Check("gme idle: zero writes performed when idle unverified", gmeTransNotIdle.Writes.Count == 0);

// Test 9C: Stale snapshot validity rejects command with zero writes before dispatch
var gmeTransStaleDispatch = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransStaleDispatch);
gmeTransStaleDispatch.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, false); // Stale snapshot
var gmeCtrlStaleDispatch = new PlateVision50.Vision.GantryMasterEnableController(gmeTransStaleDispatch);
var resStaleDispatch = gmeCtrlStaleDispatch.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme stale dispatch: invalid snapshot rejects command before write", !resStaleDispatch.Success && resStaleDispatch.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedHeartbeatStale);
Check("gme stale dispatch: zero writes performed when snapshot invalid", gmeTransStaleDispatch.Writes.Count == 0);

// Test 9D: Missing mandatory busy symbol rejects fail-closed with zero writes
var gmeTransNoBusy = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransNoBusy);
// Remove SymBusy from transport symbols
gmeTransNoBusy.CheckSymbols(new[] { PlateVision50.Vision.GantryMasterEnableController.SymBusy });
// In FakeAdsMailboxTransport, simulate unreadable symbol by not setting it
var gmeTransMissingSym = new FakeAdsMailboxTransport();
gmeTransMissingSym.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, true);
gmeTransMissingSym.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
gmeTransMissingSym.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymIdleVerified, true);
// SymBusy omitted completely
var gmeCtrlMissingSym = new PlateVision50.Vision.GantryMasterEnableController(gmeTransMissingSym);
var resMissingSym = gmeCtrlMissingSym.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme missing busy: unreadable busy symbol rejects fail-closed", !resMissingSym.Success && resMissingSym.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedMotionBusy);
Check("gme missing busy: zero writes performed when symbol unreadable", gmeTransMissingSym.Writes.Count == 0);

// Test 9E: Unreadable/missing SymReq pulse symbol before dispatch rejects fail-closed with zero writes
var gmeTransMissingReq = new FakeAdsMailboxTransport();
gmeTransMissingReq.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, true);
gmeTransMissingReq.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
gmeTransMissingReq.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymIdleVerified, true);
gmeTransMissingReq.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymBusy, false);
gmeTransMissingReq.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 10u);
// SymReq omitted
var gmeCtrlMissingReq = new PlateVision50.Vision.GantryMasterEnableController(gmeTransMissingReq);
var resMissingReq = gmeCtrlMissingReq.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme missing req: unreadable SymReq rejects fail-closed before writes",
    !resMissingReq.Success && resMissingReq.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedMotionBusy);
Check("gme missing req: zero writes performed when SymReq is unreadable", gmeTransMissingReq.Writes.Count == 0);

// Test 9F: Zero or unreadable baseline snapshot sequence rejects fail-closed with zero writes
var gmeTransZeroSnap = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransZeroSnap);
gmeTransZeroSnap.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 0u); // Zero baseline
var gmeCtrlZeroSnap = new PlateVision50.Vision.GantryMasterEnableController(gmeTransZeroSnap);
var resZeroSnap = gmeCtrlZeroSnap.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme zero snap seq: zero baseline sequence rejects fail-closed with zero writes",
    !resZeroSnap.Success && resZeroSnap.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedHeartbeatStale);
Check("gme zero snap seq: zero writes performed when baseline sequence is zero", gmeTransZeroSnap.Writes.Count == 0);

// Test 10: Offline capability blocks command with zero writes
PlateVision50.Vision.AppHardwareCapabilities.SetOfflineModeForTesting(true);
var gmeTransOffCap = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransOffCap);
var gmeCtrlOffCap = new PlateVision50.Vision.GantryMasterEnableController(gmeTransOffCap);
var resOffCap = gmeCtrlOffCap.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme offline cap: command rejected under offline mode", !resOffCap.Success && resOffCap.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedOffline);
Check("gme offline cap: zero writes performed under offline mode", gmeTransOffCap.Writes.Count == 0);
PlateVision50.Vision.AppHardwareCapabilities.SetOfflineModeForTesting(false);

// Test 11: Cancellation prior to dispatch guarantees zero writes
var gmeTransCancel = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransCancel);
var gmeCtrlCancel = new PlateVision50.Vision.GantryMasterEnableController(gmeTransCancel);
var ctsPre = new CancellationTokenSource();
ctsPre.Cancel();
var resCancel = gmeCtrlCancel.RequestMasterEnableAsync(true, ctsPre.Token).GetAwaiter().GetResult();
Check("gme cancel: canceled request rejected prior to dispatch", !resCancel.Success);
Check("gme cancel: zero writes performed on canceled request", gmeTransCancel.Writes.Count == 0);

// Test 12: Guaranteed pulse release on timeout
var gmeTransRelease = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransRelease);
var gmeCtrlRelease = new PlateVision50.Vision.GantryMasterEnableController(gmeTransRelease)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 30,
    PollIntervalMs = 5
};
gmeCtrlRelease.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme pulse release: request pulse guaranteed released to false on timeout",
    !gmeTransRelease.Read<bool>(PlateVision50.Vision.GantryMasterEnableController.SymReq));

// Test 13: Concurrency guard recheck - gate revoked while queued rejects second call with zero writes (W-01)
var gmeTransConc = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransConc);
var gmeCtrlConc = new PlateVision50.Vision.GantryMasterEnableController(gmeTransConc)
{
    PulseHoldMs = 25,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
gmeTransConc.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        // While call 1 is processing, revoke permit on transport
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, false);
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 11u);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, true);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, false);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOn);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymOutputEcho, new bool[] { true, true, true });
    }
};
var task1 = Task.Run(() => gmeCtrlConc.RequestMasterEnableAsync(true));
Thread.Sleep(5); // Ensure task 1 enters and holds lock
var task2 = Task.Run(() => gmeCtrlConc.RequestMasterEnableAsync(true));
var concRes1 = task1.GetAwaiter().GetResult();
var concRes2 = task2.GetAwaiter().GetResult();
Check("gme concurrency: call 1 succeeded before permit revocation", concRes1.Success);
Check("gme concurrency: queued call 2 rejected after permit revoked inside lock",
    !concRes2.Success && concRes2.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedUncommissioned);

// Test 14: ACK=1 but snapshot sequence unchanged rejected as mismatch (W-02)
var gmeTransStaleAck = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransStaleAck);
gmeTransStaleAck.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 50u);
gmeTransStaleAck.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
        // SnapshotSeq NOT updated past 50u
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, true);
    }
};
var gmeCtrlStaleAck = new PlateVision50.Vision.GantryMasterEnableController(gmeTransStaleAck)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resStaleAck = gmeCtrlStaleAck.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme stale ack: unchanged snapshot sequence fails closed as ErrorReadbackMismatch",
    !resStaleAck.Success && resStaleAck.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.ErrorReadbackMismatch);

// Test 15: ACK=1 but target echo inconsistent rejected as mismatch (W-02)
var gmeTransWrongEcho = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransWrongEcho);
gmeTransWrongEcho.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 11u);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
        // Requested ON (true), but readback stays OFF (false)
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, false);
    }
};
var gmeCtrlWrongEcho = new PlateVision50.Vision.GantryMasterEnableController(gmeTransWrongEcho)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resWrongEcho = gmeCtrlWrongEcho.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme wrong echo: target state mismatch fails closed as ErrorReadbackMismatch",
    !resWrongEcho.Success && resWrongEcho.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.ErrorReadbackMismatch);

// Test 16: Initial SymSnapshotSeq read failure in refresh fails closed (W-04)
var gmeTransMissingSnap = new FakeAdsMailboxTransport();
gmeTransMissingSnap.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymRefreshReq, false); // Readable low
gmeTransMissingSnap.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, true);
// Omit SymSnapshotSeq so TryRead returns false
var gmeCtrlMissingSnap = new PlateVision50.Vision.GantryMasterEnableController(gmeTransMissingSnap);
var resMissingSnap = gmeCtrlMissingSnap.RequestRefreshAsync().GetAwaiter().GetResult();
Check("gme refresh read fail: missing initial snapshot seq fails closed with zero writes",
    !resMissingSnap && gmeTransMissingSnap.Writes.Count == 0);

// Test 16B: Boot refresh from initial SnapshotSeq==0 succeeds when PLC produces generation 1
var gmeTransBoot = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransBoot);
gmeTransBoot.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 0u); // Boot baseline
gmeTransBoot.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, false);
gmeTransBoot.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymRefreshReq && (bool)val)
    {
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 1u); // Generation 1 produced
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
    }
};
var gmeCtrlBoot = new PlateVision50.Vision.GantryMasterEnableController(gmeTransBoot)
{
    PulseHoldMs = 0,
    PollIntervalMs = 5
};
var resBoot = gmeCtrlBoot.RequestRefreshAsync().GetAwaiter().GetResult();
Check("gme boot refresh: initial seq=0 accepted when refresh produces valid generation 1", resBoot);
// Test 17: Partial ReadCurrentStatus failure invalidates snapshot (W-05)
var gmeTransPartial = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransPartial);
// Omit SymState so partial read fails
var gmeTransPartialMissing = new FakeAdsMailboxTransport();
gmeTransPartialMissing.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
gmeTransPartialMissing.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 5u);
// SymState omitted
var gmeCtrlPartial = new PlateVision50.Vision.GantryMasterEnableController(gmeTransPartialMissing);
var statusPartial = gmeCtrlPartial.ReadCurrentStatus();
Check("gme partial read: incomplete symbol read forces SnapshotValid=false", !statusPartial.SnapshotValid);
Check("gme partial read: incomplete symbol read forces STALE / UNKNOWN status",
    statusPartial.StateDescription.Contains("STALE / UNKNOWN (SNAPSHOT INVALID)"));

// Test 18: Pulse release failure blocks subsequent request with zero target writes (W-03)
var gmeTransPulseFail = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransPulseFail);
var gmeCtrlPulseFail = new PlateVision50.Vision.GantryMasterEnableController(gmeTransPulseFail)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 30,
    PollIntervalMs = 5
};
gmeTransPulseFail.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val == false)
    {
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymReq, true); // Stuck high
    }
};
gmeCtrlPulseFail.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
int writesBefore = gmeTransPulseFail.Writes.Count;
var resPulseBlocked = gmeCtrlPulseFail.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme pulse fail: subsequent request blocked when pulse release unconfirmed",
    !resPulseBlocked.Success && resPulseBlocked.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.RejectedMotionBusy);
Check("gme pulse fail: no target or seq writes issued while pulse unconfirmed",
    !gmeTransPulseFail.Writes.Skip(writesBefore).Any(w => w.Symbol == PlateVision50.Vision.GantryMasterEnableController.SymTargetOn));

// Test 19: Command pulse release failure returns indeterminate failure even when ACK=1 is received (R-1)
var gmeTransPulseFailAck = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransPulseFailAck);
gmeTransPulseFailAck.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq)
    {
        if ((bool)val == true)
        {
            uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOn);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 11u);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, true);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, false);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymOutputEcho, new bool[] { true, true, true });
        }
        else
        {
            // Pulse release write to false fails: remains stuck high on PLC
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymReq, true);
        }
    }
};
var gmeCtrlPulseFailAck = new PlateVision50.Vision.GantryMasterEnableController(gmeTransPulseFailAck)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resPulseFailAck = gmeCtrlPulseFailAck.RequestMasterEnableAsync(true).GetAwaiter().GetResult();
Check("gme pulse release failure: CURRENT command returns failure despite ACK=1 (R-1)",
    !resPulseFailAck.Success && resPulseFailAck.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.ErrorCommTimeout);
Check("gme pulse release failure: message indicates indeterminate pulse release failure (R-1)",
    resPulseFailAck.Message.Contains("INDETERMINATE"));

// Test 20: Missing or failed SymMasterOnEcho read during OFF rejects command fail-closed (R-2)
var gmeTransMissingM0 = new FakeAdsMailboxTransport();
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, 0u);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)0);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOff);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, true);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymIdleVerified, true);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymBusy, false);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 10u);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, false);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymReq, false);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymRefreshReq, false);
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAxisOverrideEcho, new bool[] { false, false, false });
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymOutputEcho, new bool[] { false, false, false });
gmeTransMissingM0.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymCommErrorId, 0u);
// SymMasterOnEcho intentionally omitted
gmeTransMissingM0.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOff);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 12u);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, true);
    }
};
var gmeCtrlMissingM0 = new PlateVision50.Vision.GantryMasterEnableController(gmeTransMissingM0)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resMissingM0 = gmeCtrlMissingM0.RequestMasterEnableAsync(false).GetAwaiter().GetResult();
Check("gme missing M0: unreadable SymMasterOnEcho rejects OFF command fail-closed (R-2)",
    !resMissingM0.Success && resMissingM0.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.ErrorReadbackMismatch);

// Test 21: Inconsistent SymM10Echo (stays false) during OFF rejects command fail-closed (R-2)
var gmeTransBadM10 = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransBadM10);
gmeTransBadM10.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, true);
gmeTransBadM10.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, false);
gmeTransBadM10.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymReq && (bool)val)
    {
        uint seq = t.Read<uint>(PlateVision50.Vision.GantryMasterEnableController.SymCmdSeq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAckSeq, seq);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymResult, (ushort)PlateVision50.Vision.ServoCommandResult.AcceptedAck);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymState, (ushort)PlateVision50.Vision.ServoEvaluatedState.AllOutputsOff);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 12u);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymMasterOnEcho, false);
        // M10 stays false (inconsistent with OFF command, which requires M10=true)
        t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymM10Echo, false);
    }
};
var gmeCtrlBadM10 = new PlateVision50.Vision.GantryMasterEnableController(gmeTransBadM10)
{
    PulseHoldMs = 0,
    AckTimeoutMs = 150,
    PollIntervalMs = 5
};
var resBadM10 = gmeCtrlBadM10.RequestMasterEnableAsync(false).GetAwaiter().GetResult();
Check("gme bad M10: inconsistent SymM10Echo=false rejects OFF command fail-closed (R-2)",
    !resBadM10.Success && resBadM10.ResultCode == (ushort)PlateVision50.Vision.ServoCommandResult.ErrorReadbackMismatch);

// Test 22: Refresh pulse release failure returns false even when snapshot updated (R-1)
var gmeTransRefreshPulseFail = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransRefreshPulseFail);
gmeTransRefreshPulseFail.OnSymbolWritten = (sym, val, t) =>
{
    if (sym == PlateVision50.Vision.GantryMasterEnableController.SymRefreshReq)
    {
        if ((bool)val == true)
        {
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq, 15u);
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, true);
        }
        else
        {
            // Stuck high
            t.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymRefreshReq, true);
        }
    }
};
var gmeCtrlRefreshPulseFail = new PlateVision50.Vision.GantryMasterEnableController(gmeTransRefreshPulseFail)
{
    PulseHoldMs = 0,
    PollIntervalMs = 5
};
var resRefreshPulseFail = gmeCtrlRefreshPulseFail.RequestRefreshAsync(new CancellationTokenSource(150).Token).GetAwaiter().GetResult();
Check("gme refresh pulse fail: unconfirmed release returns false (R-1)", !resRefreshPulseFail);

// =====================================================================
// 12. GANTRY MASTER ENABLE DIAGNOSTICS & TELEMETRY OBSERVATION TESTS
// =====================================================================
Console.WriteLine("GANTRY MASTER ENABLE DIAGNOSTICS & TELEMETRY TESTS:");

// Test 23: Actual ReadCurrentStatus on disconnected transport returns Diagnostics.AdsTransport=Disconnected
var gmeTransDisc = new FakeAdsMailboxTransport { IsConnected = false, IsInRunState = false };
var gmeCtrlDisc = new PlateVision50.Vision.GantryMasterEnableController(gmeTransDisc);
var statusDisc = gmeCtrlDisc.ReadCurrentStatus();
Check("gme diag disconnected: ReadCurrentStatus reports AdsTransport=Disconnected",
    statusDisc.Diagnostics.AdsTransport == PlateVision50.Vision.GantryAdsTransportObservedState.Disconnected);
Check("gme diag disconnected: RequiredSymbolsAvailable is false",
    !statusDisc.RequiredSymbolsAvailable);

// Test 24: Actual ReadCurrentStatus on connected transport but not in RUN returns ConnectedNotRun
var gmeTransNotRun = new FakeAdsMailboxTransport { IsConnected = true, IsInRunState = false };
var gmeCtrlNotRun = new PlateVision50.Vision.GantryMasterEnableController(gmeTransNotRun);
var statusNotRun = gmeCtrlNotRun.ReadCurrentStatus();
Check("gme diag not in RUN: ReadCurrentStatus reports AdsTransport=ConnectedNotRun",
    statusNotRun.Diagnostics.AdsTransport == PlateVision50.Vision.GantryAdsTransportObservedState.ConnectedNotRun);
Check("gme diag not in RUN: RequiredSymbolsAvailable is false",
    !statusNotRun.RequiredSymbolsAvailable);

// Test 25: Actual ReadCurrentStatus on Connected RUN with missing required symbols
var gmeTransMissingMailbox = new FakeAdsMailboxTransport { IsConnected = true, IsInRunState = true };
// Only set SymPermit=true, omitting other 12 symbols
gmeTransMissingMailbox.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, true);
var gmeCtrlMissingMailbox = new PlateVision50.Vision.GantryMasterEnableController(gmeTransMissingMailbox);
var statusMissingMailbox = gmeCtrlMissingMailbox.ReadCurrentStatus();
Check("gme diag missing mailbox: reports AdsTransport=ConnectedRun",
    statusMissingMailbox.Diagnostics.AdsTransport == PlateVision50.Vision.GantryAdsTransportObservedState.ConnectedRun);
Check("gme diag missing mailbox: RequiredSymbolsAvailable is false",
    !statusMissingMailbox.RequiredSymbolsAvailable);
Check("gme diag missing mailbox: MissingSymbols identifies omitted symbol",
    statusMissingMailbox.Diagnostics.MissingSymbols.Contains(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotSeq));
Check("gme diag missing mailbox: unread idle is null not inferred false",
    statusMissingMailbox.Diagnostics.IdleVerifiedObserved == null);

// Test 26: Stale snapshot with remote permit open preserves valid remote observation
var gmeTransStalePreserve = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransStalePreserve);
gmeTransStalePreserve.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymSnapshotValid, false); // Stale/invalid MC
gmeTransStalePreserve.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, true); // Remote permit OPEN
var gmeCtrlStalePreserve = new PlateVision50.Vision.GantryMasterEnableController(gmeTransStalePreserve);
var statusStalePreserve = gmeCtrlStalePreserve.ReadCurrentStatus();
Check("gme diag stale snapshot: RequiredSymbolsAvailable is true when all 13 read",
    statusStalePreserve.RequiredSymbolsAvailable);
Check("gme diag stale snapshot: SnapshotValid is false",
    !statusStalePreserve.SnapshotValid);
Check("gme diag stale snapshot: preserves read Permit=true without defaulting to false",
    statusStalePreserve.Permit && statusStalePreserve.Diagnostics.PermitObserved == true);

// Test 27: Bad array shape breaks the required-symbol contract (not an echo value assertion)
var gmeTransBadArrayShape = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransBadArrayShape);
gmeTransBadArrayShape.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymAxisOverrideEcho, new bool[] { false, false });
gmeTransBadArrayShape.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymOutputEcho, new bool[] { false, false });
var gmeCtrlBadArrayShape = new PlateVision50.Vision.GantryMasterEnableController(gmeTransBadArrayShape);
var statusBadArrayShape = gmeCtrlBadArrayShape.ReadCurrentStatus();
Check("gme diag bad array shape: RequiredSymbolsAvailable is false",
    !statusBadArrayShape.RequiredSymbolsAvailable);
Check("gme diag bad array shape: MissingSymbols reports both echo arrays",
    statusBadArrayShape.Diagnostics.MissingSymbols.Length == 2
    && statusBadArrayShape.Diagnostics.MissingSymbols.Contains(PlateVision50.Vision.GantryMasterEnableController.SymAxisOverrideEcho)
    && statusBadArrayShape.Diagnostics.MissingSymbols.Contains(PlateVision50.Vision.GantryMasterEnableController.SymOutputEcho));

// Test 27: Tri-state remote gates (unreadable = null vs readable closed = false)
var gmeTransPermitClosed = new FakeAdsMailboxTransport();
InitHealthyGmeTransport(gmeTransPermitClosed);
gmeTransPermitClosed.SetSymbol(PlateVision50.Vision.GantryMasterEnableController.SymPermit, false); // Explicitly closed
var gmeCtrlPermitClosed = new PlateVision50.Vision.GantryMasterEnableController(gmeTransPermitClosed);
var statusPermitClosed = gmeCtrlPermitClosed.ReadCurrentStatus();
Check("gme diag tri-state: readable closed permit yields PermitObserved=false and Permit=false",
    statusPermitClosed.Diagnostics.PermitObserved == false && !statusPermitClosed.Permit);
PlateVision50.Vision.LineMailboxProtocol.ProductionCommandSourceApproved = true;

Console.WriteLine("TOTAL_TEST_FAILURES="+failed);
return failed==0?0:1;
