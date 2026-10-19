using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;

namespace NekoProxy.Desktop.Views;
public partial class OnboardingView : UserControl
{
    private OnboardingViewModel? _model;
    private CancellationTokenSource? _transition;
    private CancellationTokenSource? _captionTransition;
    private readonly DispatcherTimer _preferences = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _reduce;
    private string? _lastCaption;
    public ConnectionDiagram Diagram => Connection;
    private Control[] Pages => [WelcomePage, ConnectionPage, PrivacyPage];
    public OnboardingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachModel();
        _preferences.Tick += (_, _) => { if (_model?.IsOpen != true || _model.IsDone) { _preferences.Stop(); return; } if (_reduce != _model.ReduceMotion) RefreshMotion(); };
        Connection.FrameChanged += frame =>
        {
            if (_model is null) return;
            _model.CaptionKey = frame.CaptionKey;
            LocalLabel.Opacity = .3 + .7 * frame.LocalActivation;
            SshLabel.Opacity = .3 + .7 * frame.SshActivation;
            VpsLabel.Opacity = .3 + .7 * frame.VpsActivation;
            if (_lastCaption != frame.CaptionKey)
            {
                _lastCaption = frame.CaptionKey;
                if (!_reduce) _ = FadeCaptionAsync();
            }
        };
        KeyDown += async (_, e) =>
        {
            if (_model is null || _model.IsDone || ! _model.IsOpen) return;
            if (e.Source is Control source && (source == LanguageList || source.GetVisualAncestors().Contains(LanguageList))) return;
            if (e.Key == Key.Right && _model.CurrentPage < 2) { e.Handled = true; await _model.NextCommand.ExecuteAsync(null); }
            else if (e.Key == Key.Left) { e.Handled = true; await _model.PreviousCommand.ExecuteAsync(null); }
        };
    }
    private void AttachModel()
    {
        if (_model is not null) { _model.PropertyChanged -= ModelChanged; _model.TransitionRequested -= TransitionAsync; _model.ReplayRequested -= ReplayFromUser; }
        _model = DataContext as OnboardingViewModel;
        if (_model is null) return;
        _model.PropertyChanged += ModelChanged; _model.TransitionRequested += TransitionAsync; _model.ReplayRequested += ReplayFromUser;
        RefreshSession();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OnboardingViewModel.IsOpen) or nameof(OnboardingViewModel.IsDone)) RefreshSession();
        else if (e.PropertyName == nameof(OnboardingViewModel.ReduceMotion)) RefreshMotion();
        else if (e.PropertyName == nameof(OnboardingViewModel.SelectedLanguage)) UpdateDots();
    }
    private void RefreshSession()
    {
        if (_model is null) return;
        CancelTransition(); Connection.Stop(); Motif.Stop();
        DonePanel.IsVisible = _model.IsDone; Navigation.IsVisible = !_model.IsDone; SkipButton.IsVisible = !_model.IsDone;
        foreach (var (page, index) in Pages.Select((p, i) => (p, i)))
        { page.IsVisible = _model.IsOpen && !_model.IsDone && index == _model.CurrentPage; page.IsEnabled = true; page.IsHitTestVisible = true; page.Opacity = 1; page.RenderTransform = new TranslateTransform(); }
        UpdateDots();
        if (_model.IsOpen && !_model.IsDone) { _preferences.Start(); RefreshMotion(); Dispatcher.UIThread.Post(() => NextButton.Focus()); }
        else { _preferences.Stop(); if (_model.IsOpen) Dispatcher.UIThread.Post(() => DismissButton.Focus()); }
    }
    private void UpdateDots()
    { if (_model is null) return; foreach (var (dot, index) in new[] { Dot0, Dot1, Dot2 }.Select((p, i) => (p, i))) { dot.Classes.Set("active", index == _model.CurrentPage); AutomationProperties.SetName(dot, _model.Text["pageAria"].Replace("{n}", (index + 1).ToString())); } }
    private void RefreshMotion()
    {
        if (_model is null) return;
        _reduce = _model.ReduceMotion;
        Classes.Set("reduced", _reduce);
        Replay();
    }
    private void ReplayFromUser() => Replay(0);
    private void Replay(double delay = 140)
    {
        Connection.Stop(); Motif.Stop(); _lastCaption = null;
        if (_model?.IsOpen != true || _model.IsDone) return;
        if (_model.CurrentPage == 0) Motif.Begin(_reduce);
        else if (_model.CurrentPage == 1) Connection.Begin(_reduce, delay);
    }
    private async Task TransitionAsync(int previous, int current)
    {
        if (_model is null) return;
        CancelTransition(); Connection.Stop(); Motif.Stop(); UpdateDots();
        var source = Pages[previous]; var destination = Pages[current]; var direction = current >= previous ? 1 : -1;
        source.IsEnabled = false; source.IsHitTestVisible = false;
        destination.IsVisible = true; destination.IsEnabled = true; destination.IsHitTestVisible = true;
        foreach (var other in Pages.Where(p => p != source && p != destination)) other.IsVisible = false;
        var tokenSource = _transition = new CancellationTokenSource();
        Replay();
        try
        {
            if (!_reduce)
            {
                source.RenderTransform = new TranslateTransform(); destination.RenderTransform = new TranslateTransform();
                await Task.WhenAll(Animate(source, 160, 0, 1, 0, 0, -direction * 22, new PrototypeEasing { X2 = 1 }, tokenSource.Token),
                    Animate(destination, 220, 30, 0, 1, direction * 22, 0, new PrototypeEasing(), tokenSource.Token));
            }
            source.IsVisible = false; destination.Opacity = 1; destination.RenderTransform = new TranslateTransform();
        }
        catch (OperationCanceledException) { }
        finally { if (_transition == tokenSource) { _transition = null; tokenSource.Dispose(); } }
        Dispatcher.UIThread.Post(() => NextButton.Focus());
    }
    private static Task Animate(Control control, int duration, int delay, double fromOpacity, double toOpacity, double fromX, double toX, PrototypeEasing easing, CancellationToken token)
    {
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(duration), Delay = TimeSpan.FromMilliseconds(delay), Easing = easing, FillMode = FillMode.Forward };
        animation.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, fromOpacity), new Setter(TranslateTransform.XProperty, fromX) } });
        animation.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, toOpacity), new Setter(TranslateTransform.XProperty, toX) } });
        return animation.RunAsync(control, token);
    }
    private async Task FadeCaptionAsync()
    {
        _captionTransition?.Cancel(); _captionTransition?.Dispose();
        var source = _captionTransition = new CancellationTokenSource();
        try
        {
            Caption.RenderTransform = new TranslateTransform();
            var animation = new Animation { Duration = TimeSpan.FromMilliseconds(240), Easing = new PrototypeEasing { X1 = 0, X2 = .58 }, FillMode = FillMode.Forward };
            animation.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.YProperty, 3.0) } });
            animation.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.YProperty, 0.0) } });
            await animation.RunAsync(Caption, source.Token);
        }
        catch (OperationCanceledException) { }
        finally { if (_captionTransition == source) { _captionTransition = null; source.Dispose(); } }
    }
    private void CancelTransition()
    {
        _transition?.Cancel(); _transition?.Dispose(); _transition = null;
        _captionTransition?.Cancel(); _captionTransition?.Dispose(); _captionTransition = null;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelTransition(); _preferences.Stop(); Connection.Stop(); Motif.Stop();
        if (_model is not null) { _model.PropertyChanged -= ModelChanged; _model.TransitionRequested -= TransitionAsync; _model.ReplayRequested -= ReplayFromUser; _model = null; }
        base.OnDetachedFromVisualTree(e);
    }
}
