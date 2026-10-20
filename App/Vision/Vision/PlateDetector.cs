using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateVision50.Vision;

public record DetectionPoint(double X,double Y);
public record PlateResult(bool Found,double CenterPxX,double CenterPxY,
    double Xmm,double Ymm,double AngleDeg,double MmPerPixel,double Confidence,
    DetectionPoint[] GluePx,DetectionPoint[] GlueMm,Point2f[] OutlinePx,string Status)
{
    public static PlateResult None(string msg)=>new(false,0,0,0,0,0,0,0,[],[],[],msg);
}

public sealed class VisionOptions
{
    public double PlateSizeMm {get;set;}=50;
    public double HolePitchMm {get;set;}=40;
    public double HoleDiameterMm {get;set;}=5;
    public double FixedMmPerPixel {get;set;}=0;
    public double OriginPxX {get;set;}=double.NaN;
    public double OriginPxY {get;set;}=double.NaN;
    public double OffsetXmm {get;set;}=0;
    public double OffsetYmm {get;set;}=0;
    public int ThresholdMode {get;set;}=0;
    public double MinObjectAreaFraction {get;set;}=.006;
}

public static class PlateDetector
{
    private record Candidate(RotatedRect Rect,Point2f[] Corners,double Score,double Aspect,double Fill);
    public static PlateResult Analyze(Mat image,VisionOptions o)
        => AnalyzeAll(image,o).FirstOrDefault()??PlateResult.None("No square plate detected");

    public static IReadOnlyList<PlateResult> AnalyzeAll(Mat image,VisionOptions o)
    {
        if(image.Empty()||image.Width<100||image.Height<100)return [];
        using var gray=new Mat();
        if(image.Channels()==1)image.CopyTo(gray);
        else Cv2.CvtColor(image,gray,image.Channels()==4?ColorConversionCodes.BGRA2GRAY:ColorConversionCodes.BGR2GRAY);
        using var blur=new Mat();
        Cv2.GaussianBlur(gray,blur,new Size(5,5),0);
        using var kernel=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(7,7));
        var modes=o.ThresholdMode==1?new[]{true}:o.ThresholdMode==2?new[]{false}:new[]{true,false};
        var candidates=new List<Candidate>();
        foreach(var inverted in modes)
        {
            using var mask=new Mat();using var clean=new Mat();
            Cv2.Threshold(blur,mask,0,255,(inverted?ThresholdTypes.BinaryInv:ThresholdTypes.Binary)|ThresholdTypes.Otsu);
            Cv2.MorphologyEx(mask,clean,MorphTypes.Close,kernel);
            Cv2.FindContours(clean,out Point[][] contours,out _,RetrievalModes.External,ContourApproximationModes.ApproxSimple);
            foreach(var contour in contours)
            {
                double area=Math.Abs(Cv2.ContourArea(contour));
                if(contour.Length<4||area<image.Width*image.Height*o.MinObjectAreaFraction)continue;
                RotatedRect rr=Cv2.MinAreaRect(contour);
                double w=Math.Max(rr.Size.Width,rr.Size.Height);
                double h=Math.Min(rr.Size.Width,rr.Size.Height);
                if(h<20||w/image.Width>.96||h/image.Height>.96)continue;
                double aspect=w/h;
                if(aspect>1.30)continue;
                double fill=area/(w*h);
                if(fill<.64||fill>1.12)continue;
                Point2f[] corners=rr.Points();
                if(corners.Any(p=>p.X<3||p.Y<3||p.X>=image.Width-3||p.Y>=image.Height-3))continue;
                double score=area*(1-Math.Abs(1-aspect)*1.8)*Math.Min(fill,1.0);
                candidates.Add(new(rr,corners,score,aspect,fill));
            }
        }
        var unique=new List<Candidate>();
        foreach(var c in candidates.OrderByDescending(x=>x.Score))
        {
            double size=(c.Rect.Size.Width+c.Rect.Size.Height)/2;
            if(unique.Any(other=>Math.Sqrt(Math.Pow(c.Rect.Center.X-other.Rect.Center.X,2)+Math.Pow(c.Rect.Center.Y-other.Rect.Center.Y,2))<Math.Min(size,(other.Rect.Size.Width+other.Rect.Size.Height)/2)*.52))continue;
            unique.Add(c);
            if(unique.Count>=16)break;
        }
        return unique.Select(c=>Create(c,image.Width,image.Height,o)).ToArray();
    }

    private static PlateResult Create(Candidate c,int frameWidth,int frameHeight,VisionOptions o)
    {
        double side=(c.Rect.Size.Width+c.Rect.Size.Height)/2;
        double scale=o.FixedMmPerPixel>0?o.FixedMmPerPixel:o.PlateSizeMm/side;
        if(!double.IsFinite(scale)||scale<=0)return PlateResult.None("Bad calibration");
        double imageAngle=Math.Atan2(c.Corners[1].Y-c.Corners[0].Y,c.Corners[1].X-c.Corners[0].X)*180/Math.PI;
        while(imageAngle>=45)imageAngle-=90;
        while(imageAngle< -45)imageAngle+=90;
        double theta=-imageAngle; // robot Y points upward
        if(theta>=45)theta-=90;
        if(theta< -45)theta+=90;
        double rad=imageAngle*Math.PI/180;
        double cos=Math.Cos(rad),sin=Math.Sin(rad);
        double pitchPx=(o.HolePitchMm/2)/scale;
        double cx=c.Rect.Center.X,cy=c.Rect.Center.Y;
        double ox=double.IsNaN(o.OriginPxX)?frameWidth/2.0:o.OriginPxX;
        double oy=double.IsNaN(o.OriginPxY)?frameHeight/2.0:o.OriginPxY;
        var gluePx=new List<DetectionPoint>();
        var glueMm=new List<DetectionPoint>();
        foreach(var (sx,sy) in new[]{(-1,-1),(1,-1),(1,1),(-1,1)})
        {
            double dx=sx*pitchPx,dy=sy*pitchPx;
            double px=cx+dx*cos-dy*sin,py=cy+dx*sin+dy*cos;
            gluePx.Add(new(px,py));
            glueMm.Add(new((px-ox)*scale+o.OffsetXmm,(oy-py)*scale+o.OffsetYmm));
        }
        double quality=Math.Clamp((1-Math.Abs(c.Aspect-1))*c.Fill,0,1);
        return new PlateResult(true,cx,cy,(cx-ox)*scale+o.OffsetXmm,(oy-cy)*scale+o.OffsetYmm,
            theta,scale,quality,gluePx.ToArray(),glueMm.ToArray(),c.Corners,"OK");
    }

    public static Mat Draw(Mat image,PlateResult result,VisionOptions o)
        => DrawAll(image,result.Found?new[]{result}:[],o,null,null);
    public static Mat DrawAll(Mat image,IReadOnlyList<PlateResult> parts,VisionOptions o,
                             IReadOnlyDictionary<int,int>? trackByIndex=null, int? activeTrackId=null)
    {
        var dst=image.Clone();
        for(int i=0;i<parts.Count;i++)
        {
            var p=parts[i];if(!p.Found)continue;
            bool active=trackByIndex?.TryGetValue(i,out var id)==true&&activeTrackId==id;
            var green=active?new Scalar(0,230,255):new Scalar(80,238,128);
            Point[] rect=Array.ConvertAll(p.OutlinePx,pt=>new Point((int)Math.Round(pt.X),(int)Math.Round(pt.Y)));
            Cv2.Polylines(dst,new[]{rect},true,green,3);
            var center=new Point((int)p.CenterPxX,(int)p.CenterPxY);
            Cv2.DrawMarker(dst,center,green,MarkerTypes.Cross,20,2);
            for(int j=0;j<p.GluePx.Length;j++)
            {
                var hole=p.GluePx[j];
                var holePoint=new Point((int)hole.X,(int)hole.Y);
                Cv2.Circle(dst,holePoint,Math.Max(3,(int)Math.Round(o.HoleDiameterMm/(2*p.MmPerPixel))),new Scalar(255,221,75),2);
                // P1-P4 captions are rendered by crisp WPF callout OUTSIDE the plate.
                // The circle remains in pixel space to mark each physical feature.
            }
        }
        return dst;
    }
}
