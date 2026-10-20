using PlateVision50.Trigger;
using System;

namespace PlateVision50.Vision;

/// <summary>Coordinates in the conveyor frame: X forward to robot, Y left of travel, +theta CCW.
/// Calibration offsets are applied in the conveyor frame. Projection is valid only for an
/// orthographic planar camera; production requires measured transform/homography.</summary>
public sealed record BeltCoordinate(double Xmm,double Ymm,double ThetaDeg);
public static class BeltCoordinates
{
    public static BeltCoordinate Map(double px,double py,double thetaDeg,int width,int height,
        double mmPerPixel,double originX,double originY,double offsetX,double offsetY,
        BeltDirection direction)
    {
        if(!double.IsFinite(mmPerPixel)||mmPerPixel<=0)throw new ArgumentException("mmPerPixel > 0 required");
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
        // All mappings are multiples of 90 degrees. For a symmetric square,
        // edge angle modulo 90 has the same representative in every orientation.
        while(thetaDeg>=45)thetaDeg-=90;
        while(thetaDeg< -45)thetaDeg+=90;
        return new BeltCoordinate(x+offsetX,y+offsetY,thetaDeg);
    }
    public static BeltCoordinate FromDetection(PlateResult detection,VisionOptions calibration,
        int frameWidth,int frameHeight,BeltDirection direction) =>
        Map(detection.CenterPxX,detection.CenterPxY,detection.AngleDeg,frameWidth,frameHeight,
            detection.MmPerPixel,calibration.OriginPxX,calibration.OriginPxY,
            calibration.OffsetXmm,calibration.OffsetYmm,direction);
}
