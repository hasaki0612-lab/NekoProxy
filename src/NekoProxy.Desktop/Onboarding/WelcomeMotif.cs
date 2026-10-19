using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NekoProxy.Core;
namespace NekoProxy.Desktop.Onboarding;
public sealed class WelcomeMotif : Control
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _watch = new();
    private double _progress;
    public WelcomeMotif()
    {
        Width = 160; Height = 24;
        _timer.Tick += (_, _) => { _progress = OnboardingMotion.Ease((_watch.Elapsed.TotalMilliseconds - 180) / 700); InvalidateVisual(); if (_watch.ElapsedMilliseconds >= 880) Stop(); };
    }
    public void Begin(bool reduce) { Stop(); _progress = reduce ? 1 : 0; InvalidateVisual(); if (!reduce) { _watch.Restart(); _timer.Start(); } }
    public void Stop() { _timer.Stop(); _watch.Stop(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Stop(); base.OnDetachedFromVisualTree(e); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var line = new Pen(new SolidColorBrush(Color.Parse("#bfd0ea")), 2) { LineCap = PenLineCap.Round };
        context.DrawLine(line, new(16, 12), new(16 + 128 * _progress, 12));
        context.DrawEllipse(Brushes.White, line, new(12, 12), 4, 4);
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#eef3fb")), new Pen(new SolidColorBrush(Color.Parse("#86a6d6")), 2), new(80, 12), 7, 7);
        context.DrawEllipse(Brushes.White, line, new(148, 12), 4, 4);
    }
}
