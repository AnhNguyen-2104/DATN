using PlateVision50.Trigger;
using System.Collections.Generic;
using System.Linq;
namespace PlateVision50.Vision;

/// <summary>Coordinates in the conveyor frame: X forward to robot, Y left of travel, +theta CCW.
/// Calibration offsets are applied in the conveyor frame. Projection is valid only for an
/// orthographic planar camera; production requires measured transform/homography.</summary>
public sealed record BeltCoordinate(double Xmm,double Ymm,double ThetaDeg);

public sealed record ConveyorVertex(
    short Index,
    double X_ConveyorMm,
    double Y_ConveyorMm);

public sealed record TrackedWorkpiecePose(
    int ProductId,
    string SourceTrackKey,
    DateTime UtcAtCrossing,
    double EncoderAtCrossingMm,
    double CenterX_mm,
    double CenterY_mm,
    double Theta_deg,
    double MeasuredPitchX_mm,
    double MeasuredPitchY_mm,
    double Confidence,
    ConveyorVertex[] Vertices,
    bool IsOpticalMeasured);
public static class BeltCoordinates
{
    public static double NormalizeTheta(double thetaDeg)
    {
        if (!double.IsFinite(thetaDeg)) throw new ArgumentException("thetaDeg must be finite");
        while (thetaDeg >= 45.0) thetaDeg -= 90.0;
        while (thetaDeg < -45.0) thetaDeg += 90.0;
        return thetaDeg;
    }

    public static double ToConveyorTheta(double opticalThetaDeg, BeltDirection direction)
    {
        if (!double.IsFinite(opticalThetaDeg)) throw new ArgumentException("opticalThetaDeg must be finite");
        double rawConv = direction switch
        {
            BeltDirection.XPlus => opticalThetaDeg,
            BeltDirection.XMinus => opticalThetaDeg + 180.0,
            BeltDirection.YPlus => opticalThetaDeg + 90.0,
            BeltDirection.YMinus => opticalThetaDeg - 90.0,
            _ => opticalThetaDeg
        };
        return NormalizeTheta(rawConv);
    }

    public static BeltCoordinate Map(double px,double py,double thetaDeg,int width,int height,
        double mmPerPixel,double originX,double originY,double offsetX,double offsetY,
        BeltDirection direction)
    {
        if(!double.IsFinite(mmPerPixel)||mmPerPixel<=0)throw new ArgumentException("mmPerPixel > 0 required");
        if(!double.IsFinite(px)||!double.IsFinite(py))throw new ArgumentException("Pixel coordinates must be finite");
        if(!double.IsFinite(thetaDeg))throw new ArgumentException("thetaDeg must be finite");
        if((!double.IsNaN(originX)&&!double.IsFinite(originX))||(!double.IsNaN(originY)&&!double.IsFinite(originY)))
            throw new ArgumentException("Origin coordinates must be finite or NaN");
        if(!double.IsFinite(offsetX)||!double.IsFinite(offsetY))throw new ArgumentException("Offset coordinates must be finite");

        double ox=double.IsNaN(originX)?width/2.0:originX;
        double oy=double.IsNaN(originY)?height/2.0:originY;
        double u=(px-ox)*mmPerPixel; // image right
        double v=(oy-py)*mmPerPixel; // image up
        (double x,double y)=direction switch
        {
            BeltDirection.XPlus=>(u,v),
            BeltDirection.XMinus=>(-u,-v),
            BeltDirection.YMinus=>(v,-u),
            BeltDirection.YPlus=>(-v,u),
            _=>(u,v)
        };
        double convTheta = ToConveyorTheta(thetaDeg, direction);
        return new BeltCoordinate(x+offsetX,y+offsetY,convTheta);
    }
    public static BeltCoordinate FromDetection(PlateResult detection,VisionOptions calibration,
        int frameWidth,int frameHeight,BeltDirection direction) =>
        Map(detection.CenterPxX,detection.CenterPxY,detection.AngleDeg,frameWidth,frameHeight,
            detection.MmPerPixel,calibration.OriginPxX,calibration.OriginPxY,
            calibration.OffsetXmm,calibration.OffsetYmm,direction);

    public static ConveyorVertex[] MapVertices(
        DetectionPoint[] opticalPoints,
        double thetaDeg,
        int width,
        int height,
        double mmPerPixel,
        double originX,
        double originY,
        double offsetX,
        double offsetY,
        BeltDirection direction)
    {
        if (opticalPoints == null || opticalPoints.Length != 4)
            throw new ArgumentException("opticalPoints must contain exactly 4 points");
        if (!double.IsFinite(mmPerPixel) || mmPerPixel <= 0)
            throw new ArgumentException("mmPerPixel > 0 required");
        if (!double.IsFinite(thetaDeg))
            throw new ArgumentException("thetaDeg must be finite");
        if ((!double.IsNaN(originX) && !double.IsFinite(originX)) || (!double.IsNaN(originY) && !double.IsFinite(originY)))
            throw new ArgumentException("Origin coordinates must be finite or NaN");
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
            throw new ArgumentException("Offset coordinates must be finite");
        if (!opticalPoints.All(p => p != null && double.IsFinite(p.X) && double.IsFinite(p.Y)))
            throw new ArgumentException("Optical coordinates must be finite");

        var mapped = new (double X, double Y)[4];
        for (int i = 0; i < 4; i++)
        {
            var coord = Map(opticalPoints[i].X, opticalPoints[i].Y, thetaDeg, width, height, mmPerPixel, originX, originY, offsetX, offsetY, direction);
            mapped[i] = (coord.Xmm, coord.Ymm);
        }

        double cx = mapped.Average(p => p.X);
        double cy = mapped.Average(p => p.Y);

        double convTheta = ToConveyorTheta(thetaDeg, direction);
        double rad = convTheta * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);

        var assigned = new List<ConveyorVertex>();
        foreach (var p in mapped)
        {
            double dx = p.X - cx;
            double dy = p.Y - cy;
            // Unrotate by -convTheta into the workpiece frame
            double lx = dx * cos + dy * sin;
            double ly = -dx * sin + dy * cos;

            if (Math.Abs(lx) < 1e-4 && Math.Abs(ly) < 1e-4)
                throw new ArgumentException("Degenerate collinear or coincident points detected");

            short idx = (lx >= 0, ly >= 0) switch
            {
                (true, true) => 1,   // Quad I: Front-Left (+halfX, +halfY)
                (false, true) => 2,  // Quad II: Rear-Left (-halfX, +halfY)
                (false, false) => 3, // Quad III: Rear-Right (-halfX, -halfY)
                (true, false) => 4   // Quad IV: Front-Right (+halfX, -halfY)
            };
            assigned.Add(new ConveyorVertex(idx, p.X, p.Y));
        }

        var distinctIndices = assigned.Select(v => v.Index).Distinct().Count();
        if (distinctIndices != 4)
        {
            // Fallback: assign strictly 1..4 by polar angle
            var sortedByAngle = mapped
                .Select(p => {
                    double dx = p.X - cx, dy = p.Y - cy;
                    double lx = dx * cos + dy * sin, ly = -dx * sin + dy * cos;
                    double angle = Math.Atan2(ly, lx);
                    return (Point: p, Angle: angle);
                })
                .OrderBy(item => item.Angle)
                .ToArray();
            // Atan2 order from -PI to PI: Quad III -> Quad IV -> Quad I -> Quad II
            // Map explicitly to 3, 4, 1, 2
            short[] angleOrder = [3, 4, 1, 2];
            return sortedByAngle
                .Select((item, i) => new ConveyorVertex(angleOrder[i], item.Point.X, item.Point.Y))
                .OrderBy(v => v.Index)
                .ToArray();
        }

        return assigned.OrderBy(v => v.Index).ToArray();
    }

    public static TrackedWorkpiecePose CreateTrackedPose(
        int productId,
        string source,
        PlateResult detection,
        VisionOptions calibration,
        int frameWidth,
        int frameHeight,
        BeltDirection direction,
        double encoderAtCrossingMm = 0.0,
        DateTime? utcCrossing = null)
    {
        if (!double.IsFinite(calibration.HolePitchMm) || calibration.HolePitchMm <= 0)
            throw new ArgumentException("calibration.HolePitchMm must be finite and > 0");

        if (detection == null || !detection.Found || detection.GluePx == null || detection.GluePx.Length != 4)
        {
            return new TrackedWorkpiecePose(
                productId,
                $"{source}/{productId}",
                utcCrossing ?? DateTime.UtcNow,
                encoderAtCrossingMm,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                detection?.Confidence ?? 0.0,
                Array.Empty<ConveyorVertex>(),
                false);
        }

        var centerCoord = FromDetection(detection, calibration, frameWidth, frameHeight, direction);
        var vertices = MapVertices(detection.GluePx, detection.AngleDeg, frameWidth, frameHeight,
            detection.MmPerPixel, calibration.OriginPxX, calibration.OriginPxY,
            calibration.OffsetXmm, calibration.OffsetYmm, direction);

        static double Dist(ConveyorVertex a, ConveyorVertex b) =>
            Math.Sqrt(Math.Pow(a.X_ConveyorMm - b.X_ConveyorMm, 2) + Math.Pow(a.Y_ConveyorMm - b.Y_ConveyorMm, 2));

        double pitchX = (Dist(vertices[0], vertices[1]) + Dist(vertices[3], vertices[2])) / 2.0;
        double pitchY = (Dist(vertices[1], vertices[2]) + Dist(vertices[0], vertices[3])) / 2.0;

        bool validPitch = Math.Abs(pitchX - calibration.HolePitchMm) <= 2.0 && Math.Abs(pitchY - calibration.HolePitchMm) <= 2.0;
        bool isMeasured = detection.Found && validPitch && detection.Confidence >= 0.70;

        return new TrackedWorkpiecePose(
            productId,
            $"{source}/{productId}",
            utcCrossing ?? DateTime.UtcNow,
            encoderAtCrossingMm,
            centerCoord.Xmm,
            centerCoord.Ymm,
            centerCoord.ThetaDeg,
            pitchX,
            pitchY,
            detection.Confidence,
            vertices,
            isMeasured);
    }
}
