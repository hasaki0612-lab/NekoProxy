using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace NekoProxy.Desktop.Controls;

// Native environmental backdrop + vector surfaces/rims, not optical refraction.
// One material instance follows the palette. No continuous blur or redraw loop.
public sealed class GlassSurface : ExperimentalAcrylicBorder
{
    public static readonly StyledProperty<bool> SimplifiedProperty=AvaloniaProperty.Register<GlassSurface,bool>(nameof(Simplified));
    public bool Simplified {get=>GetValue(SimplifiedProperty);set=>SetValue(SimplifiedProperty,value);}
    private static readonly Pen Edge = new(new SolidColorBrush(Color.Parse("#34A7BEDE")), .75);
    private static readonly Pen LightEdge = new(new SolidColorBrush(Color.Parse("#4DFFFFFF")), .6);
    private readonly ExperimentalAcrylicMaterial _material = new() { BackgroundSource=AcrylicBackgroundSource.Digger };
    private readonly List<IDisposable> _bindings = [];
    public GlassSurface()
    {
        Material=_material;
        AttachedToVisualTree+=(_,_)=>BindPalette();
        DetachedFromVisualTree+=(_,_)=>ClearBindings();
        PropertyChanged+=(_,e)=>{if(e.Property==SimplifiedProperty)BindPalette();};
    }
    private void ClearBindings(){foreach(var binding in _bindings)binding.Dispose();_bindings.Clear();}
    private void BindPalette()
    {
        ClearBindings();
        _bindings.Add(_material.Bind(ExperimentalAcrylicMaterial.TintColorProperty,this.GetResourceObservable("MaterialTintColor")));
        _bindings.Add(_material.Bind(ExperimentalAcrylicMaterial.FallbackColorProperty,this.GetResourceObservable("MaterialFallbackColor")));
        if(Simplified){_material.TintOpacity=1;_material.MaterialOpacity=1;}
        else
        {
            _bindings.Add(_material.Bind(ExperimentalAcrylicMaterial.TintOpacityProperty,this.GetResourceObservable("MaterialTintOpacity")));
            _bindings.Add(_material.Bind(ExperimentalAcrylicMaterial.MaterialOpacityProperty,this.GetResourceObservable("MaterialDensity")));
        }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawLine(Edge, new Point(0,Bounds.Height-.5), new Point(Bounds.Width,Bounds.Height-.5));
        // Side catches are subpixel and short; no full-width white reflection stripe.
        if(!Simplified){context.DrawLine(LightEdge,new(.6,4),new(.6,Math.Min(22,Bounds.Height-4)));context.DrawLine(LightEdge,new(Bounds.Width-.6,4),new(Bounds.Width-.6,Math.Min(22,Bounds.Height-4)));}
    }
}
