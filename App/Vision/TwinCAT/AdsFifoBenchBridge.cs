using TwinCAT.Ads;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace PlateVision50.TwinCAT;

public sealed record FifoGlue(double X,double Y);
public sealed record FifoSnapshot(short Count,uint HeadProduct,short HeadState,double TravelMm,
    double DistanceToWorkMm,double HeadXmm,double HeadYmm,bool Inside,uint Accepted,uint Missed,
    double EncoderMmPerSec,double EncoderTravelMm,bool SimMode,bool SafeMode,int ErrorCode,
    FifoGlue[] GluePoints);

/// <summary>
/// ADS simulation-only handshake with deployed FB_CameraProductQueue.
/// Never writes robot command bits, Z, QD75, valve, or live Camera-verified flags.
/// Any unexpected motion permission causes REFUSAL.
/// </summary>
public sealed class AdsFifoBenchBridge
{
    private readonly SemaphoreSlim _lock=new(1,1);
    private static readonly string Prefix="MAIN.";
    private static T Read<T>(AdsClient ads,string name)
    {
        var h=ads.CreateVariableHandle(Prefix+name);
        try{return (T)ads.ReadAny(h,typeof(T));}
        finally{ads.DeleteVariableHandle(h);}
    }
    private static void Write(AdsClient ads,string name,object value)
    {
        var h=ads.CreateVariableHandle(Prefix+name);
        try{ads.WriteAny(h,value);}
        finally{ads.DeleteVariableHandle(h);}
    }
    private static bool SafetyCheck(AdsClient ads)
    {
        bool robotSafe=!Read<bool>(ads,"bLegacyMotionPublisherAllow")
            &&!Read<bool>(ads,"bStartGlueCycle")
            &&!Read<bool>(ads,"bAutoDispatchEnable")
            &&!Read<bool>(ads,"bAutoDispatchArm")
            &&!Read<bool>(ads,"bXYManualPermit")
            &&!Read<bool>(ads,"bDebugXYPermit")
            &&Read<bool>(ads,"bQueueSimMode")
            &&Math.Abs(Read<double>(ads,"rConveyorSpeed"))<0.5;
        return robotSafe;
    }
    public string InspectLiveReadiness()
    {
        // READ ONLY. Never writes bVisionFeedVerified, calibration or motion flags.
        using var ads=new AdsClient();
        ads.Connect(851);
        if(ads.ReadState().AdsState!=AdsState.Run)
            return "NOT READY: PLC port 851 is not in RUN state.";
        bool vision=Read<bool>(ads,"bVisionFeedVerified");
        bool calibration=Read<bool>(ads,"bVisionCalibrationOK");
        bool simulation=Read<bool>(ads,"bQueueSimMode");
        bool robotWriteEnabled=Read<bool>(ads,"bLegacyMotionPublisherAllow");
        double distance=Read<double>(ads,"rCameraToWorkStart_mm");
        double velocity=Read<double>(ads,"rConveyorSpeed");
        bool error=Read<bool>(ads,"bQueueInputFault");
        short fifoError=Read<short>(ads,"nQueueErrorCode");
        return $"ADS ONLINE (read only) | Live verified={vision} | Calibration PLC={calibration} | "+
            $"Camera→Work={distance:F2}mm | Sim={simulation} | "+
            $"MotionPublisher={robotWriteEnabled} | Speed={velocity:F2}mm/s | "+
            $"FIFO fault={error} code={fifoError}. "+
            "REAL TRANSFER NOT PERMITTED: FrameID/exposure-encoder synchronization must be confirmed, "+
            "calibration verified and a supervised dry run completed.";
    }

    public FifoSnapshot ReadStatus()
    {
        using var ads=new AdsClient();
        ads.Connect(851);
        return new(
            Read<short>(ads,"nQueueCount"),
            Read<uint>(ads,"nQueueHeadProductID"),
            Read<short>(ads,"nQueueHeadState"),
            Read<double>(ads,"rQueueHeadTravel_mm"),
            Read<double>(ads,"rQueueToWorkStart_mm"),
            Read<double>(ads,"rQueueHeadX_mm"),
            Read<double>(ads,"rQueueHeadY_mm"),
            Read<bool>(ads,"bQueueHeadAllInside"),
            Read<uint>(ads,"nQueueAccepted"),
            Read<uint>(ads,"nQueueMissed"),
            Read<double>(ads,"rConveyorSpeed"),
            Read<double>(ads,"rConveyorDist"),
            Read<bool>(ads,"bQueueSimMode"),
            SafetyCheck(ads),
            Read<short>(ads,"nQueueErrorCode"),
            Enumerable.Range(1,4).Select(i=>new FifoGlue(
                Read<double>(ads,$"aQueueHeadCornerNow[{i}].X"),
                Read<double>(ads,$"aQueueHeadCornerNow[{i}].Y"))).ToArray());
    }
    public async Task<string> EnqueueBenchAsync(double centerXmm,double centerYmm,double thetaDeg,
        CancellationToken cancellationToken=default)
    {
        if(!double.IsFinite(centerXmm)||!double.IsFinite(centerYmm)||!double.IsFinite(thetaDeg))
            throw new ArgumentException("Invalid image coordinates");
        if(Math.Abs(centerXmm)>1000||Math.Abs(centerYmm)>1000||Math.Abs(thetaDeg)>45)
            throw new ArgumentException("Uncalibrated/out-of-range frame");
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Keep the sequence short and ordered. One frame pending at a time (no hardware FrameID).
            return await Task.Run(async ()=>
            {
                using var ads=new AdsClient();
                ads.Connect(851);
                if(ads.ReadState().AdsState!=AdsState.Run)throw new InvalidOperationException("TwinCAT PLC not RUN");
                if(!SafetyCheck(ads))throw new InvalidOperationException("PLC must be BENCH SIM and all robot dispatch permits OFF");
                if(Read<bool>(ads,"bQueueSimExposure")||Read<bool>(ads,"bQueueSimResult"))
                    throw new InvalidOperationException("Camera simulation pulse still active");
                if(Read<bool>(ads,"bQueueInputFault")||Read<short>(ads,"nQueueErrorCode")!=0)
                    throw new InvalidOperationException("FIFO input latched fault/error");
                if(Read<short>(ads,"nQueueCount")>=8)throw new InvalidOperationException("FIFO FULL 8/8");
                if(Read<double>(ads,"rCameraToWorkStart_mm")<=0)
                    throw new InvalidOperationException("Camera-to-Robot distance is zero");
                // Only glue-point pitch is used in TwinCAT; physical plate outline is 50mm.
                if(Math.Abs(Read<double>(ads,"rProductLength")-40)>0.2 ||
                   Math.Abs(Read<double>(ads,"rProductWidth")-40)>0.2)
                    throw new InvalidOperationException("Tracking target pitch must be 40x40 mm");
                uint oldAccepted=Read<uint>(ads,"nQueueAccepted");
                Write(ads,"rCameraCenterX",centerXmm);
                Write(ads,"rCameraCenterY",centerYmm);
                Write(ads,"rCameraAngleDeg",thetaDeg);
                try
                {
                    // PLC exposure edge latches ENCODER distance at its own next scan.
                    // Video files and physical encoder are NOT time-synchronized.
                    Write(ads,"bQueueSimExposure",true);
                    await Task.Delay(75,cancellationToken);
                    Write(ads,"bQueueSimExposure",false);
                    if(!SafetyCheck(ads))throw new InvalidOperationException("Safety state changed during handshake");
                    Write(ads,"bQueueSimResult",true);
                    await Task.Delay(75,cancellationToken);
                    Write(ads,"bQueueSimResult",false);
                    uint nowAccepted=Read<uint>(ads,"nQueueAccepted");
                    if(nowAccepted!=oldAccepted+1)
                        throw new InvalidOperationException($"FIFO acknowledgment absent: {oldAccepted} -> {nowAccepted}");
                    return $"FIFO ACK Product Accepted #{nowAccepted}";
                }
                finally
                {
                    // Cleanup event pins only. Never reset PLC fault or other control bits.
                    try{Write(ads,"bQueueSimExposure",false);}catch{}
                    try{Write(ads,"bQueueSimResult",false);}catch{}
                }
            },cancellationToken);
        }
        finally{_lock.Release();}
    }
}
