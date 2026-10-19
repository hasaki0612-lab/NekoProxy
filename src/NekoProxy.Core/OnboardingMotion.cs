namespace NekoProxy.Core;

public sealed record ConnectionDot(double X, double Y, double Opacity, int Direction);
public sealed record ConnectionFrame(double FirstGrowth, double SecondGrowth, double Offset, double RingIntersection,
    double LocalActivation, double SshActivation, double VpsActivation,
    double SshHighlight, double VpsHighlight, string CaptionKey, IReadOnlyList<ConnectionDot> Dots);

// Direct mathematical port of the approved HTML CONFIG and renderAt. Pure,
// deterministic geometry can be checked without a renderer or a remote server.
public static class OnboardingMotion
{
    public const double Radius = 46;
    public const double DataStart = 2350;
    public const double DataPeriod = 1250;
    public const double DataSpeed = .13;
    public static double Clamp(double v) => Math.Clamp(v, 0, 1);
    public static double Ease(double x, double x1 = .4, double y1 = 0, double x2 = .2, double y2 = 1)
    {
        x = Clamp(x);
        var cx = 3 * x1; var bx = 3 * (x2 - x1) - cx; var ax = 1 - cx - bx;
        var cy = 3 * y1; var by = 3 * (y2 - y1) - cy; var ay = 1 - cy - by;
        double SX(double t) => ((ax * t + bx) * t + cx) * t;
        double SY(double t) => ((ay * t + by) * t + cy) * t;
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = SX(t) - x;
            if (Math.Abs(error) < 1e-7) return SY(t);
            var derivative = (3 * ax * t + 2 * bx) * t + cx;
            if (Math.Abs(derivative) < 1e-7) break;
            t -= error / derivative;
        }
        var low = 0.0; var high = 1.0; t = x;
        for (var i = 0; i < 24; i++) { var e = SX(t); if (Math.Abs(e - x) < 1e-7) break; if (e < x) low = t; else high = t; t = (low + high) / 2; }
        return SY(t);
    }
    public static ConnectionFrame Evaluate(double milliseconds, bool reduceMotion = false)
    {
        var t = reduceMotion ? 1e6 : Math.Max(0, milliseconds);
        double Node(double start, double duration) => Ease((t - start) / duration, .3, 0, .2, 1);
        double Pulse(double start, double peak, double rest)
        {
            var elapsed = t - start;
            if (elapsed <= 0) return 0;
            if (elapsed < 240) return peak * Ease(elapsed / 240, 0, 0, .2, 1);
            if (elapsed < 940) return peak + (rest - peak) * Ease((elapsed - 240) / 700);
            return rest;
        }
        var offset = 11 * Ease((t - 1900) / 560, .45, 0, .2, 1);
        var intersection = Math.Sqrt(Radius * Radius - offset * offset);
        var length = 560 - 2 * intersection;
        var enter = 280 - 2 * intersection; var exit = enter + 2 * intersection;
        var dots = new List<ConnectionDot>(10);
        if (!reduceMotion && t >= DataStart)
        {
            foreach (var direction in new[] { 1, -1 })
            {
                var elapsed = t - DataStart - (direction == -1 ? .5 * DataPeriod : 0);
                var max = (int)Math.Floor(elapsed / DataPeriod);
                var min = Math.Max(0, (int)Math.Floor((elapsed - length / DataSpeed) / DataPeriod));
                for (var k = min; k <= max && dots.Count(d => d.Direction == direction) < 5; k++)
                {
                    var p = DataSpeed * (elapsed - k * DataPeriod);
                    if (p < 0 || p > length) continue;
                    var fade = Math.Min(p / 14, (length - p) / 14);
                    fade = p < enter ? Math.Min(fade, (enter - p) / 14) : p > exit ? Math.Min(fade, (p - exit) / 14) : 0;
                    fade = Clamp(fade); fade = fade * fade * (3 - 2 * fade);
                    if (fade < .01) continue;
                    dots.Add(new(direction == 1 ? 120 + intersection + p : 680 - intersection - p, 60 - direction * offset, fade, direction));
                }
            }
        }
        return new(Ease((t - 260) / 680), Ease((t - 1040) / 680), offset, intersection,
            Node(0, 260), Node(940, 240), Node(1720, 240), Pulse(940, 1, .65), Pulse(1720, .7, .35),
            t >= 1900 ? "captions.ready" : t >= 260 ? "captions.tunnel" : "captions.connecting", dots);
    }
}
