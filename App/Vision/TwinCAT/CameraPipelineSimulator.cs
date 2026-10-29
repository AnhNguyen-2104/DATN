using System;
using System.Collections.Generic;
using System.Linq;
using PlateVision50.Vision;

namespace PlateVision50.TwinCAT;

/// <summary>
/// Pure software TwinCAT FB_CameraProductQueue contract emulator.
/// No AdsClient, port, device, motion, or mutable PLC state.
/// The virtual encoder and calibration are explicit simulation inputs.
/// </summary>
/// <summary>
/// 15-word coordinate and speed command block sent from TwinCAT 3 to Mitsubishi Q06UDEH (D100..D114).
/// Models the exact contract from FB_QD75CommandPublisher.TcPOU and ST_QD75_Command.TcDUT without ADS writes.
/// </summary>
public sealed record TwinCatQd75Command(
    int TargetX_0_1um,
    int TargetY_0_1um,
    int TargetZDown_0_1um,
    int SpeedApproach_0_01mmMin,
    int SpeedSync_0_01mmMin,
    int DeltaXSync_0_1um,
    int TargetZSafe_0_1um,
    short VertexIndex,
    double TargetX_mm,
    double TargetY_mm,
    double DeltaXSync_mm,
    bool IsValid,
    int ErrorCode)
{
    public ushort[] ToMelsecWords()
    {
        var words = new ushort[15];
        static void PackDint(int val, ushort[] dst, int offset)
        {
            uint u = unchecked((uint)val);
            dst[offset] = (ushort)(u & 0xFFFF);
            dst[offset + 1] = (ushort)((u >> 16) & 0xFFFF);
        }
        PackDint(TargetX_0_1um, words, 0);           // D100..D101
        PackDint(TargetY_0_1um, words, 2);           // D102..D103
        PackDint(TargetZDown_0_1um, words, 4);       // D104..D105
        PackDint(SpeedApproach_0_01mmMin, words, 6); // D106..D107
        PackDint(SpeedSync_0_01mmMin, words, 8);     // D108..D109
        PackDint(DeltaXSync_0_1um, words, 10);       // D110..D111
        PackDint(TargetZSafe_0_1um, words, 12);      // D112..D113
        words[14] = unchecked((ushort)VertexIndex); // D114
        return words;
    }
}

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
    public Receipt Accept(
        string sourceTrackKey,
        double xMm,
        double yMm,
        double thetaDegrees,
        double encoderAtTriggerMm,
        ConveyorVertex[]? measuredVertices = null)
    {
        if(string.IsNullOrWhiteSpace(sourceTrackKey)||
           !new[]{xMm,yMm,thetaDegrees,encoderAtTriggerMm}.All(double.IsFinite) ||
           Math.Abs(xMm)>1000 || Math.Abs(yMm)>1000 || Math.Abs(thetaDegrees)>45)
            throw new ArgumentException("Invalid input to simulation queue");
        if(_acceptedKeys.Contains(sourceTrackKey))
            throw new InvalidOperationException("Duplicate Track ID: already accepted");
        if(_queue.Count>=8)throw new InvalidOperationException("FIFO full 8/8");

        double yaw=CameraYawDegrees*Math.PI/180.0;
        double cosYaw=Math.Cos(yaw), sinYaw=Math.Sin(yaw);
        double xc=-CameraToWorkStartMm+CameraFineXmm+xMm*cosYaw-yMm*sinYaw;
        double yc=CameraOffsetYmm+xMm*sinYaw+yMm*cosYaw;

        FifoGlue[] corners;
        if (measuredVertices != null)
        {
            if (measuredVertices.Length != 4)
                throw new ArgumentException("measuredVertices must contain exactly 4 vertices");
            if (measuredVertices.Any(v => v is null || !double.IsFinite(v.X_ConveyorMm) || !double.IsFinite(v.Y_ConveyorMm)))
                throw new ArgumentException("measuredVertices coordinates must be finite");
            var sorted = measuredVertices.OrderBy(v => v.Index).ToArray();
            for (short i = 1; i <= 4; i++)
            {
                if (sorted[i - 1].Index != i)
                    throw new ArgumentException("measuredVertices must contain canonical indices 1..4");
            }
            corners = sorted.Select(v => new FifoGlue(
                -CameraToWorkStartMm + CameraFineXmm + v.X_ConveyorMm * cosYaw - v.Y_ConveyorMm * sinYaw,
                CameraOffsetYmm + v.X_ConveyorMm * sinYaw + v.Y_ConveyorMm * cosYaw)).ToArray();
        }
        else
        {
            double radians=(thetaDegrees+CameraYawDegrees)*Math.PI/180.0;
            double c=Math.Cos(radians), s=Math.Sin(radians);
            double halfX=PitchXmm/2.0,halfY=PitchYmm/2.0;
            var locals=new[]{(halfX,halfY),(-halfX,halfY),(-halfX,-halfY),(halfX,-halfY)};
            corners=locals.Select(p=>new FifoGlue(
                xc+p.Item1*c-p.Item2*s,
                yc+p.Item1*s+p.Item2*c)).ToArray();
        }

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
    public bool IsHeadInsideWorkspace(double currentEncoderMm, double xMin = 0.0, double xMax = 180.0, double yMin = 0.0, double yMax = 170.0)
    {
        var head = GetHead(currentEncoderMm);
        if (head is null) return false;
        return head.Corners.All(c => c.X >= xMin && c.X <= xMax && c.Y >= yMin && c.Y <= yMax);
    }
    public FifoSnapshot GetSnapshot(double currentEncoderMm = 0, double speedMmS = 0)
    {
        var head = GetHead(currentEncoderMm);
        bool inside = IsHeadInsideWorkspace(currentEncoderMm);
        double travel = head != null ? currentEncoderMm - _queue.Peek().ExposureEncoder : 0.0;
        double distToWork = head != null ? Math.Max(0.0, -head.RobotX) : 0.0;
        return new FifoSnapshot(
            (short)_queue.Count,
            head?.ProductId ?? 0,
            (short)(head == null ? 0 : inside ? 2 : 1),
            travel,
            distToWork,
            head?.RobotX ?? 0.0,
            head?.RobotY ?? 0.0,
            inside,
            _accepted,
            0,
            speedMmS,
            currentEncoderMm,
            true,
            true,
            0,
            head?.Corners ?? Array.Empty<FifoGlue>());
    }
    public TwinCatQd75Command BuildQd75Command(
        int vertexIndex,
        double conveyorSpeedMmS,
        double currentEncoderMm,
        double approachSpeedMmS = 500.0,
        double flyTimeS = 0.120,
        double syncTimeS = 0.060,
        double zDownMm = 4.0,
        double zSafeMm = 0.0,
        double xMinMm = 0.0,
        double xMaxMm = 180.0,
        double yMinMm = 0.0,
        double yMaxMm = 170.0,
        double zDownMaxMm = 52.0)
    {
        if (_queue.Count == 0)
            return new TwinCatQd75Command(0, 0, 0, 0, 0, 0, 0, (short)vertexIndex, 0, 0, 0, false, 1);

        if (vertexIndex < 1 || vertexIndex > 4
            || !double.IsFinite(currentEncoderMm)
            || !double.IsFinite(conveyorSpeedMmS) || conveyorSpeedMmS <= 0.0 || conveyorSpeedMmS > 1000.0
            || !double.IsFinite(approachSpeedMmS) || approachSpeedMmS <= 0.0 || approachSpeedMmS > 1000.0
            || !double.IsFinite(flyTimeS) || flyTimeS < 0.0 || flyTimeS > 1.0
            || !double.IsFinite(syncTimeS) || syncTimeS <= 0.0 || syncTimeS > 0.5
            || !double.IsFinite(zSafeMm) || zSafeMm < 0.0
            || !double.IsFinite(zDownMm) || !double.IsFinite(zDownMaxMm) || zDownMm > zDownMaxMm || zDownMm < zSafeMm
            || !double.IsFinite(xMinMm) || !double.IsFinite(xMaxMm) || !double.IsFinite(yMinMm) || !double.IsFinite(yMaxMm))
        {
            return new TwinCatQd75Command(0, 0, 0, 0, 0, 0, 0, (short)vertexIndex, 0, 0, 0, false, 501);
        }

        var head = GetHead(currentEncoderMm);
        if (head is null)
            return new TwinCatQd75Command(0, 0, 0, 0, 0, 0, 0, (short)vertexIndex, 0, 0, 0, false, 1);

        var corner = head.Corners[vertexIndex - 1];
        double leadMm = conveyorSpeedMmS * flyTimeS;
        double deltaXSyncMm = conveyorSpeedMmS * syncTimeS;
        double targetXMm = corner.X + leadMm;
        double targetYMm = corner.Y;

        if (targetXMm < xMinMm || (targetXMm + deltaXSyncMm) > xMaxMm
            || targetYMm < yMinMm || targetYMm > yMaxMm)
        {
            return new TwinCatQd75Command(0, 0, 0, 0, 0, 0, 0, (short)vertexIndex, targetXMm, targetYMm, deltaXSyncMm, false, 501);
        }

        int targetX_0_1um = (int)Math.Round(targetXMm * 10000.0);
        int targetY_0_1um = (int)Math.Round(targetYMm * 10000.0);
        int targetZDown_0_1um = (int)Math.Round(zDownMm * 10000.0);
        int speedApproach = (int)Math.Round(approachSpeedMmS * 6000.0);
        int speedSync = (int)Math.Round(conveyorSpeedMmS * 6000.0);
        int deltaXSync = (int)Math.Round(deltaXSyncMm * 10000.0);
        int targetZSafe = (int)Math.Round(zSafeMm * 10000.0);

        return new TwinCatQd75Command(
            targetX_0_1um,
            targetY_0_1um,
            targetZDown_0_1um,
            speedApproach,
            speedSync,
            deltaXSync,
            targetZSafe,
            (short)vertexIndex,
            targetXMm,
            targetYMm,
            deltaXSyncMm,
            true,
            0);
    }
}
