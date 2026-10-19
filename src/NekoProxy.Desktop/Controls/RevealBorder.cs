using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using NekoProxy.Desktop.Onboarding;
namespace NekoProxy.Desktop.Controls;

// One cancellable fade per modal. Interrupted fades cannot hide a reopened dialog.
public sealed class RevealBorder : Border
{
    public static readonly StyledProperty<bool> IsShownProperty=AvaloniaProperty.Register<RevealBorder,bool>(nameof(IsShown));
    public static readonly StyledProperty<bool> ReduceMotionProperty=AvaloniaProperty.Register<RevealBorder,bool>(nameof(ReduceMotion));
    public bool IsShown{get=>GetValue(IsShownProperty);set=>SetValue(IsShownProperty,value);}
    public bool ReduceMotion{get=>GetValue(ReduceMotionProperty);set=>SetValue(ReduceMotionProperty,value);}
    public bool ToastMotion {get;set;}
    private CancellationTokenSource? _fade;
    private int _generation;
    public RevealBorder(){IsVisible=false;DetachedFromVisualTree+=(_,_)=>_fade?.Cancel();}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {base.OnPropertyChanged(change);if(change.Property==IsShownProperty)_=RevealAsync(IsShown);}
    private async Task RevealAsync(bool shown)
    {
        var generation=++_generation;_fade?.Cancel();
        using var fade=new CancellationTokenSource();_fade=fade;
        IsHitTestVisible=shown;
        if(ReduceMotion||new MotionPreferences().SystemReducesMotion||VisualRoot is null){IsVisible=shown;Opacity=1;_fade=null;return;}
        var wasVisible=IsVisible;IsVisible=true;
        if(ToastMotion)
        {
            var from=wasVisible?Opacity:0;var start=wasVisible&&RenderTransform is Avalonia.Media.TranslateTransform previous?previous.Y:-8;
            var shift=new Avalonia.Media.TranslateTransform(0,start);RenderTransform=shift;var watch=System.Diagnostics.Stopwatch.StartNew();var duration=shown?270d:225d;
            try{while(true){fade.Token.ThrowIfCancellationRequested();var t=Math.Clamp(watch.Elapsed.TotalMilliseconds/duration,0,1);var q=shown?1-Math.Pow(1-t,3):t*t*(3-2*t);Opacity=from+((shown?1d:0d)-from)*q;shift.Y=start+((shown?0d:-6d)-start)*q;if(t>=1)break;await Task.Delay(16,fade.Token);}}
            catch(OperationCanceledException){}
            finally{if(generation==_generation){IsVisible=IsShown;Opacity=1;shift.Y=0;_fade=null;}}
            return;
        }
        var animation=new Animation{Easing=new Avalonia.Animation.Easings.CubicEaseOut(),Duration=TimeSpan.FromMilliseconds(160),Children={
            new KeyFrame{Cue=new Cue(0),Setters={new Setter(OpacityProperty,shown?0d:Opacity)}},
            new KeyFrame{Cue=new Cue(1),Setters={new Setter(OpacityProperty,shown?1d:0d)}}}};
        try{await animation.RunAsync(this,fade.Token);}
        catch(OperationCanceledException){}
        finally{if(generation==_generation){IsVisible=IsShown;Opacity=1;_fade=null;}}
    }
}
