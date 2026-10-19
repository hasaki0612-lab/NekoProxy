using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
using NekoProxy.Desktop.Onboarding;
namespace NekoProxy.Desktop.ViewModels;
public sealed record WindowSizeChoice(string Label)
{
    public override string ToString() => Localization.LocalizationHub.Current.Translate(Label);
}
public sealed record ThemeChoice(string Label);
public partial class SettingsViewModel(ISettingsStore store, IPlatformActions platform, string? logDirectory = null) : CategorizedViewModel
{
    public IReadOnlyList<ThemeChoice> Themes {get;}=[new("跟随系统（默认）"),new("浅色 · 清透"),new("深色 · 冷灰")];
    [ObservableProperty] private int themeIndex;
    public UpdateViewModel Updates {get;}=new(store,platform);
    [ObservableProperty] private string exportDirectory = "";
    [ObservableProperty] private string timeout = "15";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool reduceMotion;
    [ObservableProperty] private bool simplifiedVisuals;
    [ObservableProperty] private LanguageChoice selectedLanguage = TranslationCatalog.Languages[0];
    [ObservableProperty] private int windowPresetIndex = 1;
    private bool _loading;
    public IReadOnlyList<LanguageChoice> Languages => TranslationCatalog.Languages;
    public IReadOnlyList<WindowSizeChoice> WindowSizes { get; } = [new("紧凑 · 960 × 640"), new("标准 · 1060 × 720"), new("宽敞 · 1200 × 800"), new("自定义")];
    public string OnboardingLanguageName => SelectedLanguage.Name;
    public event Action<LanguageChoice>? LanguageRequested;
    public event Action<string>? WindowPresetRequested;
    public event Action? RewatchRequested;
    public event Action? ExitRequested;
    public event Action? PrivacyRequested;
    public AppSettings Current { get; private set; } = new();
    public event Action<AppSettings>? Saved;
    public void Load(AppSettings settings)
    {
        Current = settings; ThemeIndex=ThemePreference.Selection(settings.Theme); ExportDirectory = settings.ExportDirectory; Timeout = settings.SshTimeoutSeconds.ToString(); ReduceMotion = settings.ReduceMotion;SimplifiedVisuals=settings.SimplifiedVisuals;UpdatePreferences(settings);
    }
    public void UpdatePreferences(AppSettings settings)
    {
        Current = settings; _loading = true; SelectedLanguage = Languages.First(x => x.Code == TranslationCatalog.Normalize(settings.Language));
        WindowPresetIndex = Array.IndexOf(new[] { "compact", "standard", "spacious", "custom" }, settings.WindowPreset);
        if (WindowPresetIndex < 0) WindowPresetIndex = 3; _loading = false; OnPropertyChanged(nameof(OnboardingLanguageName));
    }
    public void SetLanguage(LanguageChoice choice) { _loading = true; SelectedLanguage = choice; _loading = false; OnPropertyChanged(nameof(OnboardingLanguageName)); }
    public void SetWindowPreset(string value) { _loading = true; WindowPresetIndex = Array.IndexOf(new[] { "compact", "standard", "spacious", "custom" }, value); _loading = false; }
    partial void OnSelectedLanguageChanged(LanguageChoice value) { OnPropertyChanged(nameof(OnboardingLanguageName)); if (!_loading && value is not null) LanguageRequested?.Invoke(value); }
    partial void OnWindowPresetIndexChanged(int value) { if (!_loading && value is >= 0 and <= 3) WindowPresetRequested?.Invoke(new[] { "compact", "standard", "spacious", "custom" }[value]); }
    [RelayCommand] private void Rewatch() => RewatchRequested?.Invoke();
    [RelayCommand] private void Exit() => ExitRequested?.Invoke();
    [RelayCommand] private void Privacy() => PrivacyRequested?.Invoke();
    [RelayCommand] private async Task SaveAsync()
    {
        if (!int.TryParse(Timeout, out var seconds) || seconds is < 5 or > 120) { Message = "SSH 超时应为 5–120 秒。"; return; }
        if (string.IsNullOrWhiteSpace(ExportDirectory) || !Path.IsPathFullyQualified(ExportDirectory)) { Message = "请输入完整的绝对导出路径。"; return; }
        try { var next = await store.UpdateAsync(latest => latest with { Theme=ThemePreference.FromSelection(ThemeIndex), ExportDirectory = Path.GetFullPath(ExportDirectory.Trim()), SshTimeoutSeconds = seconds, ReduceMotion = ReduceMotion,SimplifiedVisuals=SimplifiedVisuals }); Current = next; Saved?.Invoke(next); Message = "设置已保存，下次启动会恢复。"; }
        catch { Message = "设置保存失败，请检查路径与权限。"; }
    }
    [RelayCommand] private async Task OpenLogsAsync()
    { try { await platform.OpenDirectoryAsync(logDirectory ?? Path.Combine(ApplicationDirectories.Current().State, "logs")); Message = "已打开日志目录。"; } catch { Message = "无法打开日志目录。"; } }
}
