using Avalonia.Animation.Easings;
using NekoProxy.Core;
namespace NekoProxy.Desktop.Onboarding;
public sealed class PrototypeEasing : Easing
{
    public double X1 { get; set; } = .4;
    public double Y1 { get; set; }
    public double X2 { get; set; } = .2;
    public double Y2 { get; set; } = 1;
    public override double Ease(double progress) => OnboardingMotion.Ease(progress, X1, Y1, X2, Y2);
}
