using PlateVision50.Vision;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateVision50.Trigger;

public enum BeltDirection { XPlus, XMinus, YPlus, YMinus }
public sealed record TriggerEvent(int ProductId,string Source,PlateResult Pose,int FrameNumber,DateTime UtcAtCrossing,
    double PreviousProjectedPx=double.NaN,double CurrentProjectedPx=double.NaN,double MidlinePx=double.NaN);
public sealed record TrackedPose(int ProductId,PlateResult Pose,int FrameIndex,bool Crossed,bool Triggered);

public sealed class MidlineTracker
{
    private sealed class Track
    {
        public int Id;
        public double X,Y,Vx,Vy,Along,SizePx;
        public int LastFrame,Count;
        public bool Fired;
    }
    private readonly List<Track> _tracks=new();
    private int _nextId;
    private int _frame;
    public BeltDirection Direction {get;set;}=BeltDirection.XPlus;
    public double LineFraction {get;set;}=.5;
    public int MaxMissingFrames {get;set;}=12;
    public int TriggerCount {get;private set;}
    public void Reset(){_tracks.Clear();_frame=0;_nextId=0;TriggerCount=0;}

    public IReadOnlyList<TrackedPose> Update(string source,IReadOnlyList<PlateResult> detections,
        int imageWidth,int imageHeight,out IReadOnlyList<TriggerEvent> events)
    {
        _frame++;
        double line=Direction is BeltDirection.XPlus or BeltDirection.XMinus
            ?imageWidth*Math.Clamp(LineFraction,.1,.9)
            :imageHeight*Math.Clamp(LineFraction,.1,.9);
        int sign=Direction is BeltDirection.XPlus or BeltDirection.YPlus?1:-1;
        var valid=detections.Where(p=>p.Found).ToArray();
        _tracks.RemoveAll(t=>_frame-t.LastFrame>MaxMissingFrames);
        // Global data association: pair all TRACK↔DETECTION candidates, then use
        // the lowest prediction residual. Never let detection sorting decide identity.
        var possibilities=new List<(double Cost,int Detection,Track Track)>();
        for(int i=0;i<valid.Length;i++)
        {
            var p=valid[i];
            double side=50/Math.Max(p.MmPerPixel,1e-5);
            foreach(var tr in _tracks)
            {
                int dt=_frame-tr.LastFrame;
                if(dt<=0||dt>MaxMissingFrames)continue;
                double predictedX=tr.X+tr.Vx*dt;
                double predictedY=tr.Y+tr.Vy*dt;
                double residual=Math.Sqrt(Math.Pow(p.CenterPxX-predictedX,2)+Math.Pow(p.CenterPxY-predictedY,2));
                double sizeGate=Math.Max(20,Math.Min(200,Math.Max(side,tr.SizePx)*.75));
                double gate=sizeGate+Math.Min(40,Math.Sqrt(dt)*5);
                if(residual>=gate)continue;
                // Penalize an implausible reverse jump but allow slight measurement jitter.
                double dx=p.CenterPxX-tr.X,dy=p.CenterPxY-tr.Y;
                double along=Direction is BeltDirection.XPlus or BeltDirection.XMinus?dx:dy;
                double backward=Math.Max(0,-sign*along-3);
                double sizeChange=Math.Abs(side-tr.SizePx)/Math.Max(1,tr.SizePx);
                double cost=residual+backward*.6+sizeChange*8;
                possibilities.Add((cost,i,tr));
            }
        }
        var assigned=new Track?[valid.Length];
        var used=new HashSet<int>();
        foreach(var entry in possibilities.OrderBy(x=>x.Cost).ThenBy(x=>x.Track.Id).ThenBy(x=>x.Detection))
        {
            if(assigned[entry.Detection]!=null||!used.Add(entry.Track.Id))continue;
            assigned[entry.Detection]=entry.Track;
        }
        var matched=new List<TrackedPose>(valid.Length);
        var triggered=new List<TriggerEvent>();
        for(int i=0;i<valid.Length;i++)
        {
            var p=valid[i];
            double along=Direction is BeltDirection.XPlus or BeltDirection.XMinus?p.CenterPxX:p.CenterPxY;
            double projected=sign*(along-line);
            double side=50/Math.Max(p.MmPerPixel,1e-5);
            var tr=assigned[i];
            if(tr==null)
            {
                tr=new Track{
                    Id=++_nextId,X=p.CenterPxX,Y=p.CenterPxY,
                    Along=projected,SizePx=side,LastFrame=_frame,Count=1
                };
                _tracks.Add(tr);
                matched.Add(new(tr.Id,p,_frame,false,false));
                continue; // no phantom trigger on the first frame of a new ID
            }
            double previous=tr.Along;
            int dt=Math.Max(1,_frame-tr.LastFrame);
            bool crossed=!tr.Fired && tr.Count>=1 && previous<0 && projected>=0
                && projected-previous>=.6 && dt<=MaxMissingFrames && p.Confidence>=.70;
            double instantaneousVx=(p.CenterPxX-tr.X)/dt, instantaneousVy=(p.CenterPxY-tr.Y)/dt;
            // Blend velocity prediction to remain robust to frame-by-frame edge jitter.
            tr.Vx=.55*instantaneousVx+.45*tr.Vx;
            tr.Vy=.55*instantaneousVy+.45*tr.Vy;
            tr.X=p.CenterPxX;tr.Y=p.CenterPxY;tr.SizePx=side;
            tr.Along=projected;tr.LastFrame=_frame;tr.Count++;
            if(crossed)
            {
                tr.Fired=true;TriggerCount++;
                triggered.Add(new(tr.Id,source,p,_frame,DateTime.UtcNow,previous,projected,line));
            }
            matched.Add(new(tr.Id,p,_frame,crossed,tr.Fired));
        }
        events=triggered;
        return matched;
    }
}
