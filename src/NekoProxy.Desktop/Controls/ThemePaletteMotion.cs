using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace NekoProxy.Desktop.Controls;

// One finite vector palette clock. A reversal starts from displayed colors.
internal sealed class ThemePaletteMotion : IDisposable
{
    internal const double DurationMilliseconds=360;
    private static double EaseInOut(double t)=>t<.5 ? 4*t*t*t : 1-Math.Pow(-2*t+2,3)/2;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _elapsed = new();
    private readonly Dictionary<object, object> _displayed = [];
    private Dictionary<object, object> _from = [], _target = [];
    private bool _hooked;
    public bool Running => _clock.IsEnabled;
    public void Apply(ThemeVariant theme, bool animate)
    {
        _clock.Stop();
        if (!_hooked) { _clock.Tick += (_, _) => Tick(); _hooked = true; }
        var dictionary = (ResourceDictionary)Application.Current!.Resources.ThemeDictionaries[theme];
        // Keep the accepted outer-rim resource pipeline intact; its dynamic
        // resource resolution is part of the original appearance.
        _target = dictionary.Keys.Where(k=>k is not "GlassEdgeBrush").ToDictionary(k => k, k => Clone(dictionary[k]!));
        if (!animate || _displayed.Count == 0) { Write(1); return; }
        _from = _displayed.ToDictionary(p => p.Key, p => Clone(p.Value));
        Write(0); _elapsed.Restart(); _clock.Start();
    }
    private void Tick()
    {
        var t = Math.Clamp(_elapsed.Elapsed.TotalMilliseconds / DurationMilliseconds, 0, 1);
        Write(EaseInOut(t));
        if (t >= 1) { _clock.Stop(); _elapsed.Stop(); }
    }
    private void Write(double t)
    {
        var updates=new List<KeyValuePair<object,object?>>();
        foreach (var (key, target) in _target)
        {
            var value = Interpolate(_from.GetValueOrDefault(key) ?? target, target, t);
            // Shared mutable brushes invalidate only their consumers. No style
            // replacement, image allocation or per-control theme animation clocks.
            if (_displayed.TryGetValue(key, out var current) && current is SolidColorBrush brush && value is SolidColorBrush next)
                brush.Color = next.Color;
            else if (current is LinearGradientBrush gradient && value is LinearGradientBrush nextGradient)
                for (var i = 0; i < gradient.GradientStops.Count; i++) gradient.GradientStops[i].Color = nextGradient.GradientStops[i].Color;
            else { _displayed[key] = value; updates.Add(new(key,value)); }
        }
        if(updates.Count>0)
        {
            // Styles declared on Application resolve their dynamic resources there;
            // popup templates and window-local bindings must see the same instances.
            ((ResourceDictionary)Application.Current!.Resources).SetItems(updates);
        }
    }
    internal static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A,b.A), L(a.R,b.R), L(a.G,b.G), L(a.B,b.B));
    }
    private static object Interpolate(object from, object to, double t) => (from, to) switch
    {
        (ISolidColorBrush a, ISolidColorBrush b) => new SolidColorBrush(Mix(a.Color,b.Color,t)),
        (Color a, Color b) => Mix(a,b,t),
        (double a, double b) => a+(b-a)*t,
        (LinearGradientBrush a, LinearGradientBrush b) => new LinearGradientBrush
        {
            StartPoint=b.StartPoint, EndPoint=b.EndPoint,
            GradientStops=[..b.GradientStops.Select((s,i)=>new GradientStop(Mix(a.GradientStops[i].Color,s.Color,t),s.Offset))]
        },
        _ => to
    };
    private static object Clone(object value) => Interpolate(value,value,1);
    public void Dispose() { _clock.Stop(); _elapsed.Stop(); }
}
