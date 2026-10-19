using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NekoProxy.Desktop.Onboarding;

namespace NekoProxy.Desktop.Controls;

public static class MotionTokens
{
    public const double PageMilliseconds = 240;
    public const double ExitMilliseconds = 100;
    public const double EnterDelayMilliseconds = 40;
    public const double Travel = 10;
    public static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
}

// A single finite clock owns the transition. Children and their ViewModels stay
// attached; changing tabs never recreates forms or starts background work.
public sealed class PageDeck : Panel
{
    public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<PageDeck, int>(nameof(SelectedIndex));
    public static readonly StyledProperty<bool> ReduceMotionProperty = AvaloniaProperty.Register<PageDeck, bool>(nameof(ReduceMotion));
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public bool ReduceMotion { get => GetValue(ReduceMotionProperty); set => SetValue(ReduceMotionProperty, value); }
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private Control? _current, _outgoing;
    public bool TransitionRunning => _clock.IsEnabled;
    public long Generation { get; private set; }
    public PageDeck()
    {
        ClipToBounds = true;
        _clock.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) => Show(false);
        DetachedFromVisualTree += (_, _) => { _clock.Stop(); _elapsed.Stop(); };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property == SelectedIndexProperty) Show(true);
        if(change.Property == ReduceMotionProperty && ReduceMotion) Finish();
        if(change.Property == IsVisibleProperty && !IsVisible) Finish();
    }
    private void Show(bool animate)
    {
        if(Children.Count == 0) return;
        var next = Children[Math.Clamp(SelectedIndex, 0, Children.Count - 1)];
        if(next == _current) return;
        _clock.Stop(); Generation++;
        var old = _current; _current = next; _outgoing = old;
        foreach(var child in Children)
        {
            child.IsVisible = child == next || child == old;
            child.IsHitTestVisible = child == next;
            child.Opacity = child == old ? 1 : 0;
            child.RenderTransform = new TranslateTransform();
        }
        if(!animate || old is null || ReduceMotion || new MotionPreferences().SystemReducesMotion || !IsEffectivelyVisible)
        { Finish(); return; }
        _elapsed.Restart(); Tick(); _clock.Start();
    }
    private void Tick()
    {
        var ms = _elapsed.Elapsed.TotalMilliseconds;
        if(ms >= MotionTokens.PageMilliseconds) { Finish(); return; }
        if(_outgoing is {} old)
        {
            var t = MotionTokens.EaseOut(ms / MotionTokens.ExitMilliseconds);
            old.Opacity = 1-t;
            ((TranslateTransform)old.RenderTransform!).X = -MotionTokens.Travel * t;
        }
        if(_current is {} current)
        {
            var t = MotionTokens.EaseOut((ms-MotionTokens.EnterDelayMilliseconds)/(MotionTokens.PageMilliseconds-MotionTokens.EnterDelayMilliseconds));
            current.Opacity = t;
            ((TranslateTransform)current.RenderTransform!).X = MotionTokens.Travel*(1-t);
        }
    }
    private void Finish()
    {
        _clock.Stop(); _elapsed.Stop(); _outgoing = null;
        foreach(var child in Children)
        {
            child.IsVisible = child == _current;
            child.IsHitTestVisible = child == _current;
            child.Opacity = 1;
            if(child.RenderTransform is TranslateTransform transform) transform.X = 0;
        }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach(var child in Children) child.Measure(availableSize);
        return _current?.DesiredSize ?? default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    { foreach(var child in Children) child.Arrange(new Rect(finalSize)); return finalSize; }
}

