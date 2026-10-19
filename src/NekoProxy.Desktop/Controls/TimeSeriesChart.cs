using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using NekoProxy.Core;
using NekoProxy.Desktop.Localization;
namespace NekoProxy.Desktop.Controls;

// Native drawing: immutable samples, animation of viewport/clip only, no value tweening.
public sealed class TimeSeriesChart:Control,Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<MonitoringSample>> SamplesProperty=AvaloniaProperty.Register<TimeSeriesChart,IReadOnlyList<MonitoringSample>>(nameof(Samples),Array.Empty<MonitoringSample>());
    public static readonly StyledProperty<MonitorMetric> MetricProperty=AvaloniaProperty.Register<TimeSeriesChart,MonitorMetric>(nameof(Metric));
    public static readonly StyledProperty<int> RangeSecondsProperty=AvaloniaProperty.Register<TimeSeriesChart,int>(nameof(RangeSeconds),300);
    public static readonly StyledProperty<bool> ReduceMotionProperty=AvaloniaProperty.Register<TimeSeriesChart,bool>(nameof(ReduceMotion));
    public IReadOnlyList<MonitoringSample> Samples{get=>GetValue(SamplesProperty);set=>SetValue(SamplesProperty,value);}
    public MonitorMetric Metric{get=>GetValue(MetricProperty);set=>SetValue(MetricProperty,value);}
    public int RangeSeconds{get=>GetValue(RangeSecondsProperty);set=>SetValue(RangeSecondsProperty,value);}
    public bool ReduceMotion{get=>GetValue(ReduceMotionProperty);set=>SetValue(ReduceMotionProperty,value);}
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(1000d/30)};
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private double _start,_end,_scale=100,_fromStart,_fromEnd,_fromScale=100,_targetStart,_targetEnd,_targetScale=100,_motionAt,_duration=260,_revealAt=-1;
    private bool _moving,_pinned,_initialized;private Point? _pointer;private MonitoringSample? _selected;private DateTimeOffset? _hoverAt;
    public int RenderedVertices{get;private set;}
    public bool AnimationRunning=>_timer.IsEnabled;
    private Point _tipPosition,_tipFrom,_tipTarget;
    private double _tipOpacity,_tipFromOpacity,_tipTargetOpacity,_tipAt;
    private bool _tipMoving,_tipRequested,_tipInitialized,_tipLeft;
    private Rect Plot=>new(48,18,Math.Max(1,Bounds.Width-64),Math.Max(1,Bounds.Height-60));
    public bool HitTest(Point point)=>Plot.Contains(point);
    private bool MotionReduced=>ReduceMotion||VisualRoot is Window w&&w.Classes.Contains("systemReduced");
    public TimeSeriesChart()
    {
        Height=270;MinHeight=220;Focusable=true;ClipToBounds=true;
        ActualThemeVariantChanged+=(_,_)=>InvalidateVisual();
        _timer.Tick+=(_,_)=>{if(!IsEffectivelyVisible||VisualRoot is Window{WindowState:WindowState.Minimized}){_timer.Stop();return;}UpdateMotion();InvalidateVisual();if(!_moving&&_revealAt<0&&!_tipMoving)_timer.Stop();};
        PropertyChanged+=(_,e)=>{if(e.Property==SamplesProperty||e.Property==RangeSecondsProperty||e.Property==MetricProperty)Begin(e.Property==SamplesProperty?180:260,e.Property!=SamplesProperty);if(e.Property==ReduceMotionProperty&&ReduceMotion){_moving=false;_start=_targetStart;_end=_targetEnd;_scale=_targetScale;_revealAt=-1;_timer.Stop();InvalidateVisual();}};
        DetachedFromVisualTree+=(_,_)=>_timer.Stop();
    }
    private void Begin(double duration,bool reveal,bool requestRedraw=true)
    {
        UpdateMotion();var now=DateTimeOffset.UtcNow;_targetEnd=Math.Max(now.ToUnixTimeMilliseconds(),Samples.LastOrDefault()?.Timestamp.ToUnixTimeMilliseconds()??0);_targetStart=_targetEnd-Math.Clamp(RangeSeconds,60,86400)*1000d;
        _targetScale=Metric is MonitorMetric.Cpu or MonitorMetric.Memory or MonitorMetric.Disk?100:NiceScale(Samples.Where(s=>s.Timestamp.ToUnixTimeMilliseconds()>=_targetStart).SelectMany(s=>new[]{ChartSamples.Value(s,Metric),Metric==MonitorMetric.Network?ChartSamples.Value(s,Metric,true):null}).Where(v=>v is not null).Select(v=>v!.Value).DefaultIfEmpty(Metric==MonitorMetric.Network ? 0.1 : 100).Max()*1.12);
        if(!_initialized||MotionReduced){_start=_targetStart;_end=_targetEnd;_scale=_targetScale;_moving=false;}else{_fromStart=_start;_fromEnd=_end;_fromScale=_scale;_motionAt=_clock.Elapsed.TotalMilliseconds;_duration=duration;_moving=true;}
        _initialized=true;if(reveal&&!MotionReduced)_revealAt=_clock.Elapsed.TotalMilliseconds;
        if((_moving||_revealAt>=0)&&IsEffectivelyVisible)_timer.Start();if(requestRedraw)InvalidateVisual();
    }
    private void UpdateMotion()
    {
        var now=_clock.Elapsed.TotalMilliseconds;
        if(_tipMoving){var t=MotionReduced?1:Math.Clamp((now-_tipAt)/90,0,1);var e=1-Math.Pow(1-t,3);_tipPosition=new(_tipFrom.X+(_tipTarget.X-_tipFrom.X)*e,_tipFrom.Y+(_tipTarget.Y-_tipFrom.Y)*e);_tipOpacity=_tipFromOpacity+(_tipTargetOpacity-_tipFromOpacity)*e;if(t>=1){_tipMoving=false;if(!_tipRequested){_hoverAt=null;_selected=null;}}}
        if(_moving){var t=Math.Clamp((now-_motionAt)/_duration,0,1);var e=1-Math.Pow(1-t,3);_start=_fromStart+(_targetStart-_fromStart)*e;_end=_fromEnd+(_targetEnd-_fromEnd)*e;_scale=_fromScale+(_targetScale-_fromScale)*e;if(t>=1)_moving=false;}
        if(_revealAt>=0&&(now-_revealAt>=420||MotionReduced))_revealAt=-1;
    }
    private static double NiceScale(double value){var magnitude=Math.Pow(10,Math.Floor(Math.Log10(Math.Max(value,.001))));var q=value/magnitude;return(q<=1?1:q<=2?2:q<=5?5:10)*magnitude;}
    private IBrush Brush(string token,string fallback)=>this.TryFindResource(token,out var value)&&value is IBrush brush?brush:new SolidColorBrush(Color.Parse(fallback));
    private void Text(DrawingContext c,string text,double x,double y,double size=11,IBrush? brush=null)
    {
        using var layout=new TextLayout(text,new Typeface(LocalizationHub.Current.FontFamily),size,brush??Brush("MutedBrush","#65758B"));layout.Draw(c,new(x,y));
    }
    private double X(DateTimeOffset t)=>Plot.Left+(t.ToUnixTimeMilliseconds()-_start)/Math.Max(1,_end-_start)*Plot.Width;
    private double Y(double value)=>Plot.Bottom-value/Math.Max(.001,_scale)*Plot.Height;
    public override void Render(DrawingContext context)
    {
        base.Render(context);context.DrawRectangle(Brushes.Transparent,null,new Rect(Bounds.Size));if(!_initialized)Begin(0,false,false);UpdateMotion();var plot=Plot;var line=Brush("LineBrush","#E1EAF5");var accent=Brush("AccentBrush","#648DD5");var secondary=Brush("ChartSecondaryBrush","#8D93C2");
        for(var i=0;i<=4;i++){var value=_scale*i/4;var y=Y(value);context.DrawLine(new Pen(line,1),new(plot.Left,y),new(plot.Right,y));Text(context,value>=100?value.ToString("F0"):value.ToString("0.####"),3,y-7);}
        var labels=Bounds.Width<480?3:5;for(var i=0;i<labels;i++){var t=_start+(_end-_start)*i/(labels-1);var x=plot.Left+plot.Width*i/(labels-1);var text=DateTimeOffset.FromUnixTimeMilliseconds((long)t).ToLocalTime().ToString(RangeSeconds>1800?"HH:mm":"HH:mm:ss");Text(context,text,x-(i==0?0:i==labels-1?50:25),plot.Bottom+12,10);}
        if(!Samples.Any(s=>ChartSamples.Value(s,Metric) is not null)){Text(context,LocalizationHub.Current.Translate("还没有实际采样数据。"),plot.Left+12,plot.Top+plot.Height/2,13);return;}
        var visible=Samples.Where(s=>s.Timestamp.ToUnixTimeMilliseconds()>=_start-120000&&s.Timestamp.ToUnixTimeMilliseconds()<=_end+120000).ToArray();
        using(context.PushClip(plot))
        {
            for(var i=1;i<visible.Length;i++)if(visible[i].Timestamp-visible[i-1].Timestamp>ChartSamples.Gap(visible[i-1])||!visible[i].Snapshot.Success||!visible[i-1].Snapshot.Success)Gap(context,X(visible[i-1].Timestamp),X(visible[i].Timestamp));
            if(visible.LastOrDefault() is {} last&&DateTimeOffset.FromUnixTimeMilliseconds((long)_end)-last.Timestamp>ChartSamples.Gap(last))Gap(context,X(last.Timestamp),plot.Right);
            var reveal=_revealAt<0?1:1-Math.Pow(1-Math.Clamp((_clock.Elapsed.TotalMilliseconds-_revealAt)/420,0,1),3);RenderedVertices=0;
            using(context.PushClip(new Rect(plot.Left,plot.Top,plot.Width*reveal,plot.Height)))
            {
                DrawSeries(context,visible,false,accent);if(Metric==MonitorMetric.Network)DrawSeries(context,visible,true,secondary);
            }
            if(_hoverAt is {} at)
            {
                using var referenceOpacity=context.PushOpacity(_tipOpacity);
                var x=X(at);if(x>=plot.Left&&x<=plot.Right){context.DrawLine(new Pen(Brush("MutedBrush","#65758B"),1),new(x,plot.Top),new(x,plot.Bottom));if(_selected is {} sample){if(ChartSamples.Value(sample,Metric) is {} value)context.DrawEllipse(accent,new Pen(Brush("SurfaceBrush","#FFFFFF"),2),new(X(sample.Timestamp),Y(value)),4,4);if(Metric==MonitorMetric.Network&&ChartSamples.Value(sample,Metric,true) is {} up)context.DrawEllipse(secondary,new Pen(Brush("SurfaceBrush","#FFFFFF"),2),new(X(sample.Timestamp),Y(up)),4,4);}}
            }
        }
        if(_hoverAt is {} hover&&X(hover)>=plot.Left&&X(hover)<=plot.Right)Tooltip(context,hover);
    }
    private void DrawSeries(DrawingContext c,IReadOnlyList<MonitoringSample> rows,bool secondary,IBrush brush)
    {
        foreach(var segment in ChartSamples.Segments(rows,Metric,secondary))
        {
            var points=ChartSamples.Reduce(segment,DateTimeOffset.FromUnixTimeMilliseconds((long)_start),DateTimeOffset.FromUnixTimeMilliseconds((long)_end),(int)Plot.Width);RenderedVertices+=points.Count;
            if(points.Count==1){c.DrawEllipse(brush,null,new(X(points[0].Timestamp),Y(points[0].Value)),2,2);continue;}
            var geometry=new StreamGeometry();var tangents=ChartSamples.Tangents(points);
            using(var path=geometry.Open())
            {
                path.BeginFigure(new(X(points[0].Timestamp),Y(points[0].Value)),false);
                for(var i=1;i<points.Count;i++)
                {
                    var left=points[i-1];var right=points[i];var third=(right.Timestamp-left.Timestamp).TotalMilliseconds/3;
                    path.CubicBezierTo(new(X(left.Timestamp.AddMilliseconds(third)),Y(left.Value+tangents[i-1]*third)),new(X(right.Timestamp.AddMilliseconds(-third)),Y(right.Value-tangents[i]*third)),new(X(right.Timestamp),Y(right.Value)));
                }
                path.EndFigure(false);
            }
            c.DrawGeometry(null,new Pen(brush,1.6, lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),geometry);
        }
    }
    private void Gap(DrawingContext c,double left,double right)
    {
        left=Math.Max(Plot.Left,left);right=Math.Min(Plot.Right,right);if(right<=left)return;
        c.DrawRectangle(Brush("ChartGapBrush","#F2F5FA"),null,new Rect(left,Plot.Top,right-left,Plot.Height));
        if(right-left>45)Text(c,LocalizationHub.Current.Translate("采样中断"),left+6,Plot.Top+6,10);
    }
    private void Tooltip(DrawingContext c,DateTimeOffset at)
    {
        var width=Math.Min(244,Bounds.Width-12);var height=Metric==MonitorMetric.Network?112:94;
        // Side hysteresis prevents jitter when the pointer is close to the right edge.
        if(X(at)>Bounds.Width-width-24)_tipLeft=true;else if(X(at)<Bounds.Width-width-56)_tipLeft=false;
        var target=new Point(Math.Clamp(X(at)+(_tipLeft?-width-14:14),6,Math.Max(6,Bounds.Width-width-6)),Math.Clamp((_pointer?.Y??80)-height-12,6,Math.Max(6,Bounds.Height-height-6)));
        // Render may retarget an animated tip, but must never invalidate the
        // visual synchronously. The timer schedules the next composition pass.
        if(_tipRequested)MoveTip(target,1,false);UpdateMotion();var x=_tipPosition.X;var y=_tipPosition.Y;
        using var opacity=c.PushOpacity(_tipOpacity);
        c.DrawRectangle(Brush("SurfaceBrush","#FFFFFF"),new Pen(Brush("LineBrush","#E1EAF5"),1),new Rect(x,y,width,height),7,7);
        Text(c,at.ToLocalTime().ToString("HH:mm:ss")+(_pinned?" · "+LocalizationHub.Current.Translate("已固定"):""),x+13,y+10);
        if(_selected is not {} row){Text(c,LocalizationHub.Current.Translate("采样中断"),x+13,y+32,18,Brush("TextBrush","#202631"));Text(c,LocalizationHub.Current.Translate("此区间没有样本，不估算缺失值。"),x+13,y+66,10);return;}
        var value=ChartSamples.Value(row,Metric);var unit=Metric==MonitorMetric.SshConnection?"ms":"%";var display=Metric==MonitorMetric.Network?NetworkRateText.FormatMiB(value):$"{value:0.##} {unit}";Text(c,display,x+13,y+32,20,Brush("AccentInkBrush","#3D69AD"));
        if(Metric==MonitorMetric.Network)Text(c,"↑ "+NetworkRateText.FormatMiB(ChartSamples.Value(row,Metric,true)),x+13,y+64,12,Brush("ChartSecondaryBrush","#8D93C2"));
        Text(c,LocalizationHub.Current.Translate("采样原始值，未对数值做时间插值。"),x+13,y+height-23,10);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(_pinned)return;_pointer=e.GetPosition(this);SelectAt(_pointer.Value.X);
    }
    private void SelectAt(double x)
    {
        if(x<Plot.Left||x>Plot.Right){HideTip();return;}
        _tipRequested=true;
        var at=DateTimeOffset.FromUnixTimeMilliseconds((long)(_start+(x-Plot.Left)/Plot.Width*(_end-_start)));_selected=ChartSamples.Nearest(Samples,at,Metric);_hoverAt=_selected?.Timestamp??at;InvalidateVisual();
    }
    private void MoveTip(Point position,double opacity,bool requestRedraw=true)
    {
        if(!_tipInitialized){_tipPosition=position;_tipInitialized=true;}
        if(Math.Abs(position.X-_tipTarget.X)<.2&&Math.Abs(position.Y-_tipTarget.Y)<.2&&opacity==_tipTargetOpacity)return;
        UpdateMotion();_tipFrom=_tipPosition;_tipFromOpacity=_tipOpacity;_tipTarget=position;_tipTargetOpacity=opacity;_tipAt=_clock.Elapsed.TotalMilliseconds;_tipMoving=true;
        if(MotionReduced)UpdateMotion();else _timer.Start();if(requestRedraw)InvalidateVisual();
    }
    private void HideTip(){_tipRequested=false;MoveTip(_tipPosition,0);InvalidateVisual();}
    protected override void OnPointerExited(PointerEventArgs e){base.OnPointerExited(e);if(!_pinned){_pointer=null;HideTip();}}
    protected override void OnPointerPressed(PointerPressedEventArgs e){base.OnPointerPressed(e);Focus();_pointer=e.GetPosition(this);_pinned=false;SelectAt(_pointer.Value.X);_pinned=true;e.Handled=true;}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(e.Key==Key.Escape){_pinned=false;HideTip();e.Handled=true;return;}
        if(e.Key is not (Key.Left or Key.Right)||Samples.Count==0)return;
        var rows=Samples.Where(s=>ChartSamples.Value(s,Metric) is not null).ToArray();if(rows.Length==0)return;
        var index=_selected is null?rows.Length-1:Array.IndexOf(rows,_selected);index=Math.Clamp(index+(e.Key==Key.Right?1:-1),0,rows.Length-1);_selected=rows[index];_hoverAt=_selected.Timestamp;_pinned=true;_tipRequested=true;InvalidateVisual();e.Handled=true;
    }
}
