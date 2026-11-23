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
        using var smallKernel=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(5,5));
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
                var coarse=new Candidate(rr,corners,score,aspect,fill);
                var candidate=RefineCandidate(coarse,blur,inverted,o,image.Width,image.Height,smallKernel);
                if(candidate!=null)candidates.Add(candidate);
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

    private static Candidate? RefineCandidate(Candidate c,Mat blur,bool inverted,VisionOptions o,int imageWidth,int imageHeight,Mat smallKernel)
    {
        // Only refine if coarse candidate shows evidence of elongation from an attached halo/reflection
        if(c.Aspect<=1.06)return c;

        Point2f[] pts=c.Corners;
        float minX=pts.Min(p=>p.X);
        float maxX=pts.Max(p=>p.X);
        float minY=pts.Min(p=>p.Y);
        float maxY=pts.Max(p=>p.Y);
        int pad=8;
        int rx=Math.Max(0,(int)Math.Floor(minX-pad));
        int ry=Math.Max(0,(int)Math.Floor(minY-pad));
        int rw=Math.Min(imageWidth-rx,(int)Math.Ceiling(maxX+pad)-rx);
        int rh=Math.Min(imageHeight-ry,(int)Math.Ceiling(maxY+pad)-ry);
        if(rw<20||rh<20)return c;
        Rect roiRect=new Rect(rx,ry,rw,rh);

        using var roiBlur=new Mat(blur,roiRect);
        using var roiCandidateMask=new Mat(new Size(rw,rh),MatType.CV_8UC1,new Scalar(0));
        Point[] roiPts=Array.ConvertAll(c.Corners,p=>new Point((int)Math.Round(p.X-rx),(int)Math.Round(p.Y-ry)));
        Cv2.FillConvexPoly(roiCandidateMask,roiPts,new Scalar(255));

        using var histMat=new Mat();
        Cv2.CalcHist(new[]{roiBlur},new[]{0},roiCandidateMask,histMat,1,new[]{256},new[]{new Rangef(0,256)});
        float[] hist=new float[256];
        for(int i=0;i<256;i++)hist[i]=histMat.Get<float>(i);
        if(hist.Sum()<100)return c;

        double t1=ComputeOtsu(hist,0,255);
        double tRefined;
        ThresholdTypes threshType;

        if(!inverted)
        {
            int minV=Math.Clamp((int)Math.Ceiling(t1),0,255);
            tRefined=ComputeOtsu(hist,minV,255);
            threshType=ThresholdTypes.Binary;
        }
        else
        {
            int maxV=Math.Clamp((int)Math.Floor(t1),0,255);
            tRefined=ComputeOtsu(hist,0,maxV);
            threshType=ThresholdTypes.BinaryInv;
        }

        using var refinedMask=new Mat();
        Cv2.Threshold(roiBlur,refinedMask,tRefined,255,threshType);
        Cv2.BitwiseAnd(refinedMask,roiCandidateMask,refinedMask);

        using var refinedClean=new Mat();
        Cv2.MorphologyEx(refinedMask,refinedClean,MorphTypes.Close,smallKernel);

        Cv2.FindContours(refinedClean,out Point[][] refinedContours,out _,RetrievalModes.External,ContourApproximationModes.ApproxSimple);
        if(refinedContours.Length==0)return c;

        var bestContour=refinedContours.OrderByDescending(x=>Cv2.ContourArea(x)).First();
        double area=Math.Abs(Cv2.ContourArea(bestContour));

        // Enforce same configured min area contract: if the true core is below min area,
        // it must not be accepted as a tiny refined workpiece (reject ambiguous undersized target)
        double minArea=imageWidth*imageHeight*o.MinObjectAreaFraction;
        if(area<minArea)return null;

        RotatedRect rrLocal=Cv2.MinAreaRect(bestContour);
        RotatedRect rr=new RotatedRect(
            new Point2f(rrLocal.Center.X+rx,rrLocal.Center.Y+ry),
            rrLocal.Size,
            rrLocal.Angle
        );

        double w=Math.Max(rr.Size.Width,rr.Size.Height);
        double h=Math.Min(rr.Size.Width,rr.Size.Height);
        if(h<20||w/imageWidth>.96||h/imageHeight>.96)return c;

        double aspect=w/h;
        if(aspect>1.30)return c;
        double fill=area/(w*h);
        if(fill<.64||fill>1.12)return c;

        Point2f[] corners=rr.Points();
        if(corners.Any(p=>p.X<3||p.Y<3||p.X>=imageWidth-3||p.Y>=imageHeight-3))return c;

        // Credible body support: workpiece must retain the primary body, not collapse into an inner highlight
        double coarseMaxSide=Math.Max(c.Rect.Size.Width,c.Rect.Size.Height);
        double coarseMinSide=Math.Min(c.Rect.Size.Width,c.Rect.Size.Height);
        double coarseArea=coarseMaxSide*coarseMinSide*Math.Min(c.Fill,1.0);
        if(area<coarseArea*0.50||w<coarseMaxSide*0.60||h<coarseMinSide*0.60)return c;

        double centerShift=Math.Sqrt(Math.Pow(rr.Center.X-c.Rect.Center.X,2)+Math.Pow(rr.Center.Y-c.Rect.Center.Y,2));
        if(centerShift>w*0.35)return c;

        double coarseAspectDiff=Math.Abs(c.Aspect-1.0);
        double refinedAspectDiff=Math.Abs(aspect-1.0);

        if(refinedAspectDiff<coarseAspectDiff)
        {
            double score=area*(1-Math.Abs(1-aspect)*1.8)*Math.Min(fill,1.0);
            return new Candidate(rr,corners,score,aspect,fill);
        }

        return c;
    }

    private static double ComputeOtsu(float[] hist,int minVal,int maxVal)
    {
        float total=0;
        for(int i=minVal;i<=maxVal;i++)total+=hist[i];
        if(total<=0)return(minVal+maxVal)/2.0;

        double sum=0;
        for(int i=minVal;i<=maxVal;i++)sum+=(double)i*hist[i];

        double sumB=0;
        float wB=0;
        double maxVar=-1;
        double threshold=(minVal+maxVal)/2.0;

        for(int t=minVal;t<=maxVal;t++)
        {
            wB+=hist[t];
            if(wB<=0)continue;
            float wF=total-wB;
            if(wF<=0)break;

            sumB+=(double)t*hist[t];
            double mB=sumB/wB;
            double mF=(sum-sumB)/wF;

            double varBetween=(double)wB*wF*(mB-mF)*(mB-mF);
            if(varBetween>maxVar)
            {
                maxVar=varBetween;
                threshold=t;
            }
        }
        return threshold;
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
