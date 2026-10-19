using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
namespace NekoProxy.Desktop.Controls;
// Content layer over the single native acrylic window backdrop. Material density,
// edges and shadows are shared styles so the opaque fallback stays coordinated.
public class GlassPanel:Border
{
    private readonly Rim _rim;
    public GlassPanel(){_rim=new(this){IsHitTestVisible=false};VisualChildren.Add(_rim);ActualThemeVariantChanged+=(_,_)=>_rim.InvalidateVisual();}
    protected override Size ArrangeOverride(Size finalSize){var size=base.ArrangeOverride(finalSize);_rim.Arrange(new Rect(size));return size;}
    private sealed class Rim(GlassPanel owner):Control
    {
      public override void Render(DrawingContext context)
      {
        if(VisualRoot is Window window&&window.Classes.Contains("simpleGlass"))return;
        if(Bounds.Width<4||Bounds.Height<4||owner.CornerRadius.TopLeft<2)return;
        // Inset reflection suggests a thin transparent edge without extra blur.
        var dark=ActualThemeVariant==ThemeVariant.Dark;
        var rim=new LinearGradientBrush{StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,1,RelativeUnit.Relative),
            GradientStops=[new(Color.Parse(dark?"#60E0ECFF":"#CFFFFFFF"),0),new(Colors.Transparent,.35),new(Color.Parse(dark?"#14536786":"#228BAAD5"),.7),new(Color.Parse(dark?"#28C7DAF4":"#78FFFFFF"),1)]};
        var rect=new Rect(Bounds.Size).Deflate(1.5);var radius=Math.Max(0,owner.CornerRadius.TopLeft-1.5);
        context.DrawRectangle(null,new Pen(rim,.7),rect,radius,radius);
      }
    }
}
