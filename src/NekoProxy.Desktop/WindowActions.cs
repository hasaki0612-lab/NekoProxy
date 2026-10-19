using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NekoProxy.Core;
using NekoProxy.Desktop.ViewModels;
namespace NekoProxy.Desktop;
public partial class MainWindow
{
    private readonly DispatcherTimer _windowSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private double _normalWidth = 1060, _normalHeight = 720;
    private string _preset = "standard";
    private bool _windowReady, _applyingSize, _allowClose, _closing;
    private bool _rememberMaximized;
    private (double Width, double Height) _appliedSize = (1060,720);
    private ISettingsStore? _windowStore;
    private void RecordWindowEvent(string kind)
    {
        var index=Array.IndexOf(Program.Arguments,"--window-events");
        if(index<0 || !Program.Arguments.Contains("--data-dir"))return;
        var path=Program.Arguments[index+1]; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path,System.Text.Json.JsonSerializer.Serialize(new{Kind=kind,State=WindowState.ToString(),Width=Bounds.Width,Height=Bounds.Height,X=Position.X,Y=Position.Y,Scale=RenderScaling,Time=DateTimeOffset.UtcNow})+Environment.NewLine);
    }
    public void ConfigureWindow(ISettingsStore store)
    {
        _windowStore = store;
        Localization.LocalizationHub.Current.PropertyChanged += (_, e) => { if (e.PropertyName == "FontFamily") FontFamily = new Avalonia.Media.FontFamily(Localization.LocalizationHub.Current.FontFamily); };
        _windowSaveTimer.Tick += async (_, _) => { _windowSaveTimer.Stop(); if (DataContext is MainViewModel active && active.Privacy?.IsBusy == true) { QueueWindowSave(); return; } try { await SaveWindowAsync(); } catch { if (DataContext is MainViewModel vm) vm.Status = "窗口设置保存失败，请检查目录权限。"; } };
        SizeChanged += (_, _) =>
        {
            RecordWindowEvent("size");
            if (!_windowReady || _applyingSize || WindowState != WindowState.Normal) return;
            _normalWidth = Bounds.Width; _normalHeight = Bounds.Height;
            QueueWindowSave();
        };
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty && _windowReady) { RecordWindowEvent("state"); if(WindowState==WindowState.Minimized&&DataContext is MainViewModel monitoringVm)monitoringVm.Monitoring.Stop(); if (WindowState != WindowState.Minimized) _rememberMaximized = WindowState == WindowState.Maximized; QueueWindowSave(); } };
        PositionChanged += (_, _) => RecordWindowEvent("position");
        Screens.Changed += OnScreensChanged;
        Closing += OnWindowClosing;
        Closed += (_, _) => { Screens.Changed -= OnScreensChanged; RecordWindowEvent("closed"); _windowSaveTimer.Stop(); if (DataContext is MainViewModel vm) { vm.CloseExporterCommand.Execute(null); vm.Deployment.ResetCommand.Execute(null);vm.CancelEditorCommand.Execute(null);vm.CancelNodeEditorCommand.Execute(null);vm.Settings.Updates.Dispose(); } };
    }
    public async Task RestoreWindowAsync(MainViewModel vm)
    {
        var settings = vm.Settings.Current;
        await ApplyThemeAsync(vm,false);vm.Settings.PropertyChanged+=async(_,e)=>{if(e.PropertyName==nameof(SettingsViewModel.ThemeIndex))await ApplyThemeAsync(vm,true);};
        void ApplyMaterial()=>TransparencyLevelHint=vm.Settings.SimplifiedVisuals?[WindowTransparencyLevel.None]:[WindowTransparencyLevel.AcrylicBlur,WindowTransparencyLevel.Blur,WindowTransparencyLevel.None];
        ApplyMaterial();vm.Settings.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(SettingsViewModel.SimplifiedVisuals))ApplyMaterial();};
        _rememberMaximized = settings.WindowMaximized;
        FontFamily = new Avalonia.Media.FontFamily(Localization.LocalizationHub.Current.FontFamily);
        _preset = settings.WindowPreset; _normalWidth = double.IsFinite(settings.WindowWidth) ? Math.Clamp(settings.WindowWidth, 900, 3840) : 1060;
        _normalHeight = double.IsFinite(settings.WindowHeight) ? Math.Clamp(settings.WindowHeight, 600, 2160) : 720;
        SetVisibleSize(_normalWidth, _normalHeight);
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        vm.ExitRequested += Close;
        vm.ExitWithoutSavingRequested += () => { _allowClose=true; Close(); };
        vm.WindowPresetRequested += ApplyWindowPreset;
        vm.PickServerFileAsync = async () => { var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {Title=Localization.LocalizationHub.Current.Translate("保存服务端配置"),SuggestedFileName="NekoProxy-server.json",DefaultExtension="json",ShowOverwritePrompt=true});return file?.TryGetLocalPath(); };
        vm.Deployment.PickServerFileAsync=vm.PickServerFileAsync;
        vm.Deployment.PickPrivateKeyAsync=async()=>{var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=Localization.LocalizationHub.Current.Translate("选择 SSH 私钥"),AllowMultiple=false});return files.FirstOrDefault()?.TryGetLocalPath();};
        if (vm.Privacy is { } privacy) privacy.PickFileAsync = PickPrivacyFileAsync;
        _windowReady = true;
        await Task.CompletedTask;
    }
    private void SetVisibleSize(double width, double height)
    {
        _applyingSize = true;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var scale = screen?.Scaling ?? RenderScaling;
        var work = screen?.WorkingArea;
        if (work is { } available) { MinWidth = Math.Min(900, available.Width / scale - 8); MinHeight = Math.Min(600, available.Height / scale - 8); }
        Width = work is { } area ? Math.Min(width, area.Width / scale - 8) : width;
        Height = work is { } visible ? Math.Min(height, visible.Height / scale - 8) : height;
        if (work is { } bounds)
        {
            Position = new PixelPoint(Math.Clamp(Position.X, bounds.X, Math.Max(bounds.X, bounds.Right - (int)(Width * scale))), Math.Clamp(Position.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - (int)(Height * scale))));
        }
        _normalWidth = Width; _normalHeight = Height; _appliedSize = (Width,Height); _applyingSize = false;
    }
    private void OnScreensChanged(object? sender, EventArgs e)
    {
        if (!_windowReady) return;
        if (WindowState == WindowState.Normal) SetVisibleSize(_normalWidth,_normalHeight);
        else { var screen=Screens.ScreenFromWindow(this) ?? Screens.Primary; if (screen is not null) { _normalWidth=Math.Min(_normalWidth,screen.WorkingArea.Width/screen.Scaling-8);_normalHeight=Math.Min(_normalHeight,screen.WorkingArea.Height/screen.Scaling-8); } }
        QueueWindowSave();
    }
    public void ApplyWindowPreset(string preset)
    {
        _preset = preset;
        if (preset != "custom") { WindowState = WindowState.Normal; var size = preset switch { "compact" => (960d, 640d), "spacious" => (1200d, 800d), _ => (1060d, 720d) }; SetVisibleSize(size.Item1, size.Item2); }
        QueueWindowSave();
    }
    private void QueueWindowSave() { _windowSaveTimer.Stop(); _windowSaveTimer.Start(); }
    public async Task SaveWindowAsync()
    {
        if (_windowStore is null || !_windowReady) return;
        // Windows sends transient resize events while restoring; classify only
        // after the debounce, once the final normal bounds have settled.
        if (WindowState == WindowState.Normal && (Math.Abs(_normalWidth-_appliedSize.Width)>2 || Math.Abs(_normalHeight-_appliedSize.Height)>2)) { _preset="custom"; if(DataContext is MainViewModel vm)vm.Settings.SetWindowPreset(_preset); }
        await _windowStore.UpdateAsync(s => s with { WindowWidth = _normalWidth, WindowHeight = _normalHeight, WindowMaximized = _rememberMaximized, WindowPreset = _preset });
    }
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if(!_allowClose&&DataContext is MainViewModel active&&active.Deployment.IsOperating)
        {
            e.Cancel=true;if(_closing)return;_closing=true;
            try{active.HostTrust.Cancel();await active.Deployment.CancelAndWaitAsync();}finally{_closing=false;}
            Close();return;
        }
        if (_allowClose) return; e.Cancel = true; if (_closing) return;
        if (DataContext is MainViewModel busy && (busy.Privacy?.IsBusy == true || busy.Onboarding.IsSaving || busy.Settings.SaveCommand.IsRunning)) { busy.Status = "操作进行中，请完成后再退出。"; return; }
        _closing = true; _windowSaveTimer.Stop();
        try { if (DataContext is MainViewModel vm) { vm.HostTrust.Cancel();vm.SshSecrets.Cancel();vm.Toast.Dispose();await vm.Network.CancelAndWaitAsync();await vm.Monitoring.DisposeAsync();await vm.Advanced.DisposeAsync();vm.SshSecrets.Clear();await vm.FlushPreferencesAsync(); } await SaveWindowAsync(); _allowClose = true; Close(); }
        catch { if (DataContext is MainViewModel vm) { vm.Status = "退出前无法保存设置，请检查目录权限后重试。"; vm.IsExitSaveFailed=true; } }
        finally { _closing = false; }
    }
    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
    private void MinimizeClicked(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClicked(object? sender, RoutedEventArgs e) => ToggleMaximize();
    public void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void TitlebarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) ToggleMaximize(); else BeginMoveDrag(e);
        e.Handled = true;
    }
    private async Task<string?> PickPrivacyFileAsync(string kind)
    {
        if (kind == "restore")
        {
            var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Localization.LocalizationHub.Current.Translate("恢复加密备份"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("NekoProxy") { Patterns = ["*.nekobak"] }] });
            return chosen.FirstOrDefault()?.TryGetLocalPath();
        }
        var diagnostics = kind == "diagnostics";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = Localization.LocalizationHub.Current.Translate(diagnostics ? "导出脱敏诊断报告" : "导出加密备份"), SuggestedFileName = "NekoProxy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + (diagnostics ? ".json" : ".nekobak"), DefaultExtension = diagnostics ? "json" : "nekobak", ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }
}
