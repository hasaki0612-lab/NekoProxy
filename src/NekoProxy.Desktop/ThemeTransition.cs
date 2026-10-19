using Avalonia;
using Avalonia.Styling;
using NekoProxy.Desktop.Controls;
using NekoProxy.Desktop.ViewModels;
namespace NekoProxy.Desktop;

public partial class MainWindow
{
    private ThemePaletteMotion? _themeMotion;
    private bool _applyingTheme;
    private Task ApplyThemeAsync(MainViewModel vm, bool animate)
    {
        if (_themeMotion is null)
        {
            _themeMotion = new();
            // OS changes follow the same timeline when the user selects System.
            ActualThemeVariantChanged += (_,_) => { if (!_applyingTheme) RefreshPalette(true); };
            Classes.CollectionChanged += (_,_) =>
            {
                if (Classes.Contains("reduced") || Classes.Contains("systemReduced")) RefreshPalette(false);
            };
            Closed += (_,_) => _themeMotion.Dispose();
        }
        _applyingTheme = true;
        var theme=vm.Settings.ThemeIndex switch { 1=>ThemeVariant.Light,2=>ThemeVariant.Dark,_=>ThemeVariant.Default };
        RequestedThemeVariant=theme;
        if (Application.Current is {} app) app.RequestedThemeVariant=theme;
        _applyingTheme = false;
        RefreshPalette(animate);
        return Task.CompletedTask;
    }
    private void RefreshPalette(bool animate)
    {
        _themeMotion?.Apply(ActualThemeVariant,animate && !Classes.Contains("reduced") && !Classes.Contains("systemReduced"));
        TransparencyBackgroundFallback=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(ActualThemeVariant==ThemeVariant.Dark?"#18212D":"#EFF6FF"));
    }
    internal bool ThemeMotionRunning => _themeMotion?.Running == true;
}
