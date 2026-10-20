using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PlateVision50.Trigger;

namespace PlateVision50.Vision;

/// <summary>
/// Crisp WPF label layer kept independent of pixel-processed video frames.
/// A label is positioned outside the workpiece and the video is never upscaled
/// just to make rendered text legible. All coordinates are DIP, not source pixels.
/// </summary>
public static class VisionLabelOverlay
{
    public sealed record LabelRect(double X,double Y,double W,double H,int Index);
    public static bool Overlap(LabelRect a,LabelRect b,double padding=0)
        => a.X < b.X+b.W+padding && a.X+a.W+padding>b.X &&
           a.Y < b.Y+b.H+padding && a.Y+a.H+padding>b.Y;
    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFrom(hex)!;
    private const double LabelWidth=229, LabelHeight=159;
    // Every tracked product owns one persistent label placement. It follows
    // the object in image coordinates, not detection-list ordering.
    public sealed class LabelPlacementTracker
    {
        private enum Side { Right,Left,Above,Below }
        private sealed class State
        {
            public Side Side;
            public double X,Y;
            public int LastSeen;
            public bool HasLocation;
        }
        private readonly Dictionary<(string Source,int TrackId),State> _labels=new();
        private int _frame;
        public void Reset(){_labels.Clear();_frame=0;}
        private static LabelRect Box(PlateResult p,int idx,double s,double ox,double oy)
        {
            if(p.OutlinePx.Length==0)
                return new LabelRect(ox+p.CenterPxX*s-2,oy+p.CenterPxY*s-2,4,4,idx);
            double x0=p.OutlinePx.Min(q=>q.X),x1=p.OutlinePx.Max(q=>q.X);
            double y0=p.OutlinePx.Min(q=>q.Y),y1=p.OutlinePx.Max(q=>q.Y);
            return new LabelRect(ox+x0*s,oy+y0*s,(x1-x0)*s,(y1-y0)*s,idx);
        }
        private static bool Fits(LabelRect proposal,Side side,LabelRect owner,
            LabelRect[] all,IReadOnlyList<LabelRect> placed,double xl,double yt,double xr,double yb)
        {
            if(proposal.X<xl+3||proposal.Y<yt+3||proposal.X+proposal.W>xr-3||
               proposal.Y+proposal.H>yb-3)return false;
            // Require a real gap from the owner, not just a free rectangle on top of it.
            if(Overlap(proposal,owner,6))return false;
            foreach(var p in all)
                if(p.Index!=owner.Index && Overlap(proposal,p,6))return false;
            foreach(var l in placed)
                if(Overlap(proposal,l,8))return false;
            return true;
        }
        public IReadOnlyList<LabelRect> LayoutTracked(
            IReadOnlyList<PlateResult> parts,IReadOnlyDictionary<int,int> ids,string source,
            int imageWidth,int imageHeight,double viewportW,double viewportH,
            double triggerFraction=double.NaN,bool triggerHorizontal=true)
        {
            _frame++;
            if(imageWidth<=0||imageHeight<=0||viewportW<=0||viewportH<=0)return [];
            double scale=Math.Min(viewportW/imageWidth,viewportH/imageHeight);
            double ox=(viewportW-imageWidth*scale)/2,oy=(viewportH-imageHeight*scale)/2;
            double xr=ox+imageWidth*scale,yb=oy+imageHeight*scale;
            double triggerCoordinate=double.IsFinite(triggerFraction)
                ?(triggerHorizontal?oy+imageHeight*scale*triggerFraction:ox+imageWidth*scale*triggerFraction)
                :double.NaN;
            bool CrossesTrigger(LabelRect c) => double.IsFinite(triggerCoordinate)
                &&(triggerHorizontal
                    ?c.Y-3<=triggerCoordinate&&c.Y+c.H+3>=triggerCoordinate
                    :c.X-3<=triggerCoordinate&&c.X+c.W+3>=triggerCoordinate);
            var boxes=parts.Select((p,i)=>Box(p,i,scale,ox,oy)).ToArray();
            var results=new List<LabelRect>();
            // Stable identity order: draw priority is Track ID, not detection score.
            var order=Enumerable.Range(0,parts.Count).Where(i=>parts[i].Found&&ids.ContainsKey(i))
                .OrderBy(i=>ids[i]);
            foreach(int index in order)
            {
                int trackId=ids[index];
                var key=(source,trackId);
                bool newTrack=!_labels.TryGetValue(key,out var state);
                if(newTrack){state=new State();_labels[key]=state;}
                var workpiece=boxes[index];
                // Exactly following the material center; the same label side is preserved
                // across frames. The label never goes to an arbitrary screen corner.
                static (double X,double Y) At(Side side,LabelRect b)
                    =>side switch
                    {
                        Side.Right=>(b.X+b.W+13,b.Y+b.H/2-LabelHeight/2),
                        Side.Left=>(b.X-LabelWidth-13,b.Y+b.H/2-LabelHeight/2),
                        Side.Above=>(b.X+b.W/2-LabelWidth/2,b.Y-LabelHeight-13),
                        _=>(b.X+b.W/2-LabelWidth/2,b.Y+b.H+13)
                    };
                var priority=newTrack
                    ? new[]{Side.Right,Side.Left,Side.Above,Side.Below}
                    : new[]{state!.Side,Side.Right,Side.Left,Side.Above,Side.Below}.Distinct().ToArray();
                LabelRect? candidate=null;
                Side usedSide=Side.Right;
                // Two passes: preserve same Track ID and previous side where possible,
                // but place text away from the yellow trigger line before accepting an
                // overlap. No background panel, no connector, no screen-corner jumps.
                bool clearOfTrigger=false;
                foreach(bool preferClear in new[]{true,false})
                {
                    foreach(var side in priority)
                    {
                        var (desiredX,desiredY)=At(side,workpiece);
                        double x=(side is Side.Right or Side.Left)
                            ?desiredX:Math.Clamp(desiredX,ox+4,Math.Max(ox+4,xr-LabelWidth-4));
                        double y=(side is Side.Above or Side.Below)
                            ?desiredY:Math.Clamp(desiredY,oy+4,Math.Max(oy+4,yb-LabelHeight-4));
                        var rect=new LabelRect(x,y,LabelWidth,LabelHeight,index);
                        if(!Fits(rect,side,workpiece,boxes,results,ox,oy,xr,yb))continue;
                        if(preferClear && CrossesTrigger(rect))continue;
                        candidate=rect;usedSide=side;
                        clearOfTrigger=!CrossesTrigger(rect);
                        break;
                    }
                    if(candidate!=null)break;
                }
                if(candidate is null)
                {
                    // Do not show a foreign plate's numbers or let the label jump
                    // across the whole screen when crowded: hide it until there
                    // is a valid non-overlapping placement.
                    state!.LastSeen=_frame;
                    continue;
                }
                var chosen=candidate;
                double newX=chosen.X,newY=chosen.Y;
                if(!newTrack && state!.HasLocation && state.Side==usedSide)
                {
                    // Dampen 1–2 pixel contour jitter, but jump directly to a
                    // fast-moving plate so the text never trails a different one.
                    double dx=newX-state.X,dy=newY-state.Y;
                    if(Math.Abs(dx)<40 && Math.Abs(dy)<40)
                    {newX=state.X+.65*dx;newY=state.Y+.65*dy;}
                    var smoothed=new LabelRect(newX,newY,LabelWidth,LabelHeight,index);
                    if(Fits(smoothed,usedSide,workpiece,boxes,results,ox,oy,xr,yb)
                        &&(!clearOfTrigger || !CrossesTrigger(smoothed)))
                        chosen=smoothed;
                }
                state!.X=chosen.X;state.Y=chosen.Y;state.Side=usedSide;
                state.HasLocation=true;state.LastSeen=_frame;
                results.Add(chosen);
            }
            foreach(var k in _labels.Where(x=>_frame-x.Value.LastSeen>60).Select(x=>x.Key).ToArray())
                _labels.Remove(k);
            return results;
        }
    }

    public static IReadOnlyList<LabelRect> Layout(
        IReadOnlyList<PlateResult> parts,int imageWidth,int imageHeight,double viewportW,double viewportH)
        =>new LabelPlacementTracker().LayoutTracked(
            parts,parts.Select((_,i)=>(i,id:i+1)).ToDictionary(v=>v.i,v=>v.id),
            "layout-test",imageWidth,imageHeight,viewportW,viewportH);

    public static void Render(Canvas layer,IReadOnlyList<PlateResult> parts,
        IReadOnlyDictionary<int,int> ids,VisionOptions options,BeltDirection belt,
        int imageWidth,int imageHeight,double viewportW,double viewportH,
        LabelPlacementTracker? state=null,string source="VIDEO",double triggerFraction=.5)
    {
        layer.Children.Clear();
        if(parts.Count==0||viewportW<250||viewportH<200)return;
        var rects=(state??new LabelPlacementTracker()).LayoutTracked(parts,ids,source,
            imageWidth,imageHeight,viewportW,viewportH,triggerFraction,
            belt is BeltDirection.YPlus or BeltDirection.YMinus);
        double scale=Math.Min(viewportW/imageWidth,viewportH/imageHeight);
        double left=(viewportW-imageWidth*scale)/2,top=(viewportH-imageHeight*scale)/2;
        foreach(var r in rects)
        {
            PlateResult p=parts[r.Index];
            int track=ids.TryGetValue(r.Index,out int id)?id:(r.Index+1);
            var pose=BeltCoordinates.FromDetection(p,options,imageWidth,imageHeight,belt);
            string val(double x)=>x.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
            var display=new StackPanel{Orientation=Orientation.Vertical,Margin=new Thickness(9,6,7,5)};
            display.Children.Add(new TextBlock{
                Text=$"TRACK #{track:00}",
                FontFamily=new FontFamily("Consolas"),FontSize=12.5,FontWeight=FontWeights.Bold,
                Foreground=Brush("#FFF2F7FB")});
            display.Children.Add(new TextBlock{
                Text=$"X={val(pose.Xmm)} mm  Y={val(pose.Ymm)} mm",
                FontFamily=new FontFamily("Consolas"),FontSize=11.8,FontWeight=FontWeights.SemiBold,
                Foreground=Brush("#FFF2F7FB"),Margin=new Thickness(0,1,0,0)});
            display.Children.Add(new TextBlock{
                Text=$"Góc θ = {val(pose.ThetaDeg)}°",
                FontFamily=new FontFamily("Segoe UI"),FontSize=12.5,FontWeight=FontWeights.SemiBold,
                Foreground=Brush("#F8D477"),Margin=new Thickness(0,2,0,4)});
            for(int i=0;i<Math.Min(4,p.GluePx.Length);i++)
            {
                var gp=p.GluePx[i];
                var point=BeltCoordinates.Map(gp.X,gp.Y,p.AngleDeg,imageWidth,imageHeight,
                    p.MmPerPixel,options.OriginPxX,options.OriginPxY,options.OffsetXmm,options.OffsetYmm,belt);
                display.Children.Add(new TextBlock {
                    Text=$"P{i+1}  X={val(point.Xmm),8}  Y={val(point.Ymm),8}",
                    FontFamily=new FontFamily("Consolas"),FontSize=11.8,
                    Foreground=Brush("#BAEAF7"),Margin=new Thickness(0,1,0,0)
                });
            }
            // Chỉ giữ chữ nổi trên video: KHÔNG nền, KHÔNG viền,
            // KHÔNG đường nối. Không che phần tâm, góc và 4 lỗ của phôi.
            display.Width=LabelWidth;
            display.Height=LabelHeight;
            display.Margin=new Thickness(0);
            display.SnapsToDevicePixels=true;
            display.Effect=new System.Windows.Media.Effects.DropShadowEffect
            {
                Color=Colors.Black,
                BlurRadius=0,
                ShadowDepth=1,
                Opacity=0.9
            };
            TextOptions.SetTextFormattingMode(display,TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(display,TextRenderingMode.ClearType);
            Canvas.SetLeft(display,r.X);
            Canvas.SetTop(display,r.Y);
            layer.Children.Add(display);
        }
    }
}
