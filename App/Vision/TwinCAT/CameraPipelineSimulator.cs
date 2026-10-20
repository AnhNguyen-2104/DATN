using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateVision50.TwinCAT;

/// <summary>
/// Pure software TwinCAT FB_CameraProductQueue contract emulator.
/// No AdsClient, port, device, motion, or mutable PLC state.
/// The virtual encoder and calibration are explicit simulation inputs.
/// </summary>
public sealed class CameraPipelineSimulator
{
    public sealed record Receipt(uint ProductId, int QueueCount, double RobotX, double RobotY,
        FifoGlue[] Corners, double EncoderAtTrigger, string SourceTrackKey);
    private sealed record Item(Receipt Base, double ExposureEncoder);
    private readonly Queue<Item> _queue = new();
    private readonly HashSet<string> _acceptedKeys = new(StringComparer.Ordinal);
    private uint _accepted;
    public double CameraToWorkStartMm { get; }
    public double CameraFineXmm { get; }
    public double CameraOffsetYmm { get; }
    public double CameraYawDegrees { get; }
    public double PitchXmm { get; }
    public double PitchYmm { get; }
    public int QueueCount => _queue.Count;
    public uint Accepted => _accepted;
    public CameraPipelineSimulator(double cameraToWorkStartMm=200,double cameraFineXmm=0,
        double cameraOffsetYmm=85,double cameraYawDegrees=0,
        double pitchXmm=40,double pitchYmm=40)
    {
        if(!double.IsFinite(cameraToWorkStartMm) || cameraToWorkStartMm<=0 ||
           !double.IsFinite(cameraFineXmm) || !double.IsFinite(cameraOffsetYmm) ||
           !double.IsFinite(cameraYawDegrees) || !double.IsFinite(pitchXmm) ||
           !double.IsFinite(pitchYmm) || pitchXmm<=0 || pitchYmm<=0)
            throw new ArgumentException("Invalid virtual calibration.");
        CameraToWorkStartMm=cameraToWorkStartMm;
        CameraFineXmm=cameraFineXmm;
        CameraOffsetYmm=cameraOffsetYmm;
        CameraYawDegrees=cameraYawDegrees;
        PitchXmm=pitchXmm;PitchYmm=pitchYmm;
    }
    public Receipt Accept(string sourceTrackKey,double xMm,double yMm,double thetaDegrees,
        double encoderAtTriggerMm)
    {
        if(string.IsNullOrWhiteSpace(sourceTrackKey)||
           !new[]{xMm,yMm,thetaDegrees,encoderAtTriggerMm}.All(double.IsFinite) ||
           Math.Abs(xMm)>1000 || Math.Abs(yMm)>1000 || Math.Abs(thetaDegrees)>45)
            throw new ArgumentException("Invalid input to simulation queue");
        if(_acceptedKeys.Contains(sourceTrackKey))
            throw new InvalidOperationException("Duplicate Track ID: already accepted");
        if(_queue.Count>=8)throw new InvalidOperationException("FIFO full 8/8");
        double yaw=CameraYawDegrees*Math.PI/180.0;
        double xc=-CameraToWorkStartMm+CameraFineXmm+xMm*Math.Cos(yaw)-yMm*Math.Sin(yaw);
        double yc=CameraOffsetYmm+xMm*Math.Sin(yaw)+yMm*Math.Cos(yaw);
        double radians=(thetaDegrees+CameraYawDegrees)*Math.PI/180.0;
        double c=Math.Cos(radians), s=Math.Sin(radians);
        double halfX=PitchXmm/2.0,halfY=PitchYmm/2.0;
        var locals=new[]{(halfX,halfY),(-halfX,halfY),(-halfX,-halfY),(halfX,-halfY)};
        var corners=locals.Select(p=>new FifoGlue(
            xc+p.Item1*c-p.Item2*s,
            yc+p.Item1*s+p.Item2*c)).ToArray();
        // Prepare all fields first: then commit atomically.
        uint id=checked(_accepted+1);
        var receipt=new Receipt(id,_queue.Count+1,xc,yc,corners,encoderAtTriggerMm,sourceTrackKey);
        _queue.Enqueue(new Item(receipt,encoderAtTriggerMm));
        _acceptedKeys.Add(sourceTrackKey);
        _accepted=id;
        return receipt;
    }
    public Receipt? GetHead(double currentEncoderMm)
    {
        if(!double.IsFinite(currentEncoderMm))throw new ArgumentException("Invalid encoder");
        if(_queue.Count==0)return null;
        var item=_queue.Peek();
        double travel=currentEncoderMm-item.ExposureEncoder;
        var r=item.Base;
        return r with {
            RobotX=r.RobotX+travel,
            Corners=r.Corners.Select(p=>new FifoGlue(p.X+travel,p.Y)).ToArray(),
            QueueCount=_queue.Count
        };
    }
    public void CompleteHead()
    {
        if(_queue.Count==0)throw new InvalidOperationException("Nothing to complete");
        _queue.Dequeue();
    }
    public void Reset()
    {
        _queue.Clear();_acceptedKeys.Clear();_accepted=0;
    }
}
