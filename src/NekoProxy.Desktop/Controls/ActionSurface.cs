using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace NekoProxy.Desktop.Controls;

// Deliberately solid: operations remain readable without any platform backdrop.
public sealed class ActionSurface:GlassPanel
{
    public ActionSurface(){Margin=new Thickness(18,4,18,14);CornerRadius=new CornerRadius(16);}
}
