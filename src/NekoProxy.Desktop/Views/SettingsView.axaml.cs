using Avalonia.Controls;
namespace NekoProxy.Desktop.Views;
public partial class SettingsView : UserControl
{
    private ViewModels.SettingsViewModel? _model;
    public SettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { _model = DataContext as ViewModels.SettingsViewModel; if (_model is not null) _model.PropertyChanged += OnPreferenceChanged; };
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnPreferenceChanged; _model = null; };
    }
    private async void OnPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "SelectedLanguage" || !IsVisible || _model?.ReduceMotion == true || new Onboarding.MotionPreferences().SystemReducesMotion) return;
        var animation = new Avalonia.Animation.Animation { Duration = TimeSpan.FromMilliseconds(180), Children = {
            new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, .92d) } },
            new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } } } };
        await animation.RunAsync(this);
    }
}
