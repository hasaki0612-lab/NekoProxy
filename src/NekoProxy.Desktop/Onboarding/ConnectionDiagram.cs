using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NekoProxy.Core;
namespace NekoProxy.Desktop.Onboarding;

public sealed class ConnectionDiagram : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _stopwatch = new();
    private Window? _window;
    private double _last;
    private bool _wanted;
    private bool _reduced;
    public double ClockMilliseconds { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public int Ticks { get; private set; }
    public bool IsRunning => _timer.IsEnabled;
    public ConnectionFrame Frame => OnboardingMotion.Evaluate(ClockMilliseconds, _reduced);
    public event Action<ConnectionFrame>? FrameChanged;
    private bool Dark=>ActualThemeVariant==Avalonia.Styling.ThemeVariant.Dark;
    private IBrush Ink=>new SolidColorBrush(Color.Parse(Dark?"#E4EDF8":"#272b33"));
    private Pen Line=>new(new SolidColorBrush(Color.Parse(Dark?"#7590B1":"#bfd0ea")),2){LineCap=PenLineCap.Round};
    private static readonly Geometry Stand = Geometry.Parse("M32 44v8 M22 53h20");
    private static readonly Geometry Lock = Geometry.Parse("M21 28v-7a11 11 0 0 1 22 0v7 M32 42.5v4.5");
    private static readonly Geometry ServerMarks = Geometry.Parse("M27 19.5h5 M27 44.5h5");
    public ConnectionDiagram()
    {
        Width = 800; Height = 120;
        _timer.Tick += (_, _) =>
        {
            var now = _stopwatch.Elapsed.TotalMilliseconds;
            ClockMilliseconds += Math.Clamp(now - _last, 0, 50); _last = now; Ticks++;
            InvalidateVisual(); FrameChanged?.Invoke(Frame);
        };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _window = this.GetVisualRoot() as Window;
        if (_window is not null) _window.PropertyChanged += WindowChanged;
        Resume();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Pause(); if (_window is not null) _window.PropertyChanged -= WindowChanged; _window = null;
        base.OnDetachedFromVisualTree(e);
    }
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty)
        { if (_window?.WindowState == WindowState.Minimized || _window?.IsVisible == false) Pause(); else Resume(); }
    }
    public void Begin(bool reduced, double delay = 140)
    {
        Stop(); _wanted = true; _reduced = reduced; ClockMilliseconds = -delay;
        if (reduced) ClockMilliseconds = 1e6;
        InvalidateVisual(); FrameChanged?.Invoke(Frame); Resume();
    }
    public void Stop() { _wanted = false; Pause(); }
    private void Pause() { if (_timer.IsEnabled) { _timer.Stop(); _stopwatch.Stop(); Stops++; } }
    private void Resume()
    {
        if (!_wanted || _reduced || _timer.IsEnabled || _window is null || _window.WindowState == WindowState.Minimized || !_window.IsVisible) return;
        _stopwatch.Restart(); _last = 0; _timer.Start(); Starts++;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var f = Frame; var w = f.RingIntersection;
        void Segment(double from, double to, double growth)
        {
            if (growth <= .0005) return;
            var start = from - 3; var end = start + (to + 3 - start) * growth;
            context.DrawLine(Line, new(start, 60 - f.Offset), new(end, 60 - f.Offset));
            context.DrawLine(Line, new(start, 60 + f.Offset), new(end, 60 + f.Offset));
        }
        Segment(120 + w, 400 - w, f.FirstGrowth); Segment(400 + w, 680 - w, f.SecondGrowth);
        foreach (var dot in f.Dots)
            using (context.PushOpacity(dot.Opacity)) context.DrawEllipse(new SolidColorBrush(Color.Parse("#6b91cb")), null, new(dot.X, dot.Y), 3.4, 3.4);
        DrawNode(context, 120, f.LocalActivation, f.LocalActivation * .35, 0);
        DrawNode(context, 400, f.SshActivation, f.SshHighlight, 1);
        DrawNode(context, 680, f.VpsActivation, f.VpsHighlight, 2);
    }
    private static Color Mix(string a, string b, double t)
    {
        var x = Color.Parse(a); var y = Color.Parse(b);
        byte C(byte u, byte v) => (byte)Math.Round(u + (v - u) * t);
        return Color.FromRgb(C(x.R, y.R), C(x.G, y.G), C(x.B, y.B));
    }
    private void DrawNode(DrawingContext context, double x, double activation, double highlight, int kind)
    {
        var baseRing = Mix(Dark?"#52647C":"#eef0f4", Dark?"#7D91AB":"#e0e4ea", activation);
        var ring = new SolidColorBrush(Mix(baseRing.ToString(), Dark?"#AFC8E8":"#a8c0e4", highlight));
        var fill = new SolidColorBrush(Mix(Dark?"#202D3E":"#ffffff", Dark?"#31445E":"#eef4fc", highlight));
        context.DrawEllipse(fill, new Pen(ring, 1.5), new(x, 60), 46, 46);
        using var opacity = context.PushOpacity(.3 + .7 * activation);
        using var translation = context.PushTransform(Matrix.CreateTranslation(x - 32, 28));
        var pen = new Pen(Ink, 3) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (kind == 0)
        { context.DrawRectangle(null, pen, new(8, 11, 48, 33), 6, 6); context.DrawGeometry(null, pen, Stand); }
        else if (kind == 1)
        { context.DrawRectangle(null, pen, new(13, 28, 38, 26), 7, 7); context.DrawGeometry(null, pen, Lock); context.DrawEllipse(Ink, null, new(32, 40), 2.4, 2.4); }
        else
        {
            context.DrawRectangle(null, pen, new(9, 10, 46, 19), 6.5, 6.5); context.DrawRectangle(null, pen, new(9, 35, 46, 19), 6.5, 6.5);
            context.DrawEllipse(Ink, null, new(19, 19.5), 2.2, 2.2); context.DrawEllipse(Ink, null, new(19, 44.5), 2.2, 2.2);
            context.DrawGeometry(null, pen, ServerMarks);
        }
    }
}
