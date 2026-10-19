using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
using NekoProxy.Desktop.Onboarding;
namespace NekoProxy.Desktop.ViewModels;

public partial class OnboardingViewModel(ISettingsStore store, TranslationCatalog catalog, IMotionPreferences motion) : ObservableObject
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private bool _userReducesMotion;
    private bool _loadingLanguage;
    private string? _errorKey;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private bool isDone;
    [ObservableProperty] private bool wasSkipped;
    [ObservableProperty] private bool isRewatch;
    [ObservableProperty] private int currentPage;
    [ObservableProperty] private bool isTransitioning;
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private LanguageChoice selectedLanguage = TranslationCatalog.Languages[0];
    [ObservableProperty] private string captionKey = "captions.connecting";
    public IReadOnlyList<LanguageChoice> Languages => TranslationCatalog.Languages;
    public IReadOnlyDictionary<string, string> Text => catalog.For(SelectedLanguage.Code);
    public string Caption => Text[CaptionKey];
    public string NextLabel => Text[CurrentPage == 2 ? "start" : "next"];
    public string DoneTitle => Text[WasSkipped ? "skippedTitle" : "doneTitle"];
    public string FontFamily => SelectedLanguage.FontFamily;
    public bool CanBack => CurrentPage > 0;
    public bool CanNavigate => !IsTransitioning && !IsSaving;
    public bool ReduceMotion => _userReducesMotion || motion.SystemReducesMotion;
    public Task PendingPreferenceWrite { get; private set; } = Task.CompletedTask;
    public event Action<AppSettings>? PreferencesSaved;
    public event Func<int, int, Task>? TransitionRequested;
    public event Action? ReplayRequested;
    public void Initialize(AppSettings settings)
    {
        ApplyPreferences(settings);
        if (!settings.OnboardingHandled) Open(false);
    }
    public void ApplyPreferences(AppSettings settings)
    {
        _userReducesMotion = settings.ReduceMotion;
        _loadingLanguage = true;
        SelectedLanguage = Languages.First(l => l.Code == TranslationCatalog.Normalize(settings.Language));
        _loadingLanguage = false;
        OnPropertyChanged(nameof(ReduceMotion));
    }
    public void Open(bool rewatch)
    {
        if (IsSaving || IsTransitioning) return;
        IsRewatch = rewatch; CurrentPage = 0; IsDone = false; WasSkipped = false; SetError(null); IsOpen = true;
        // Rewatch is an independent UI session; no server repository is touched.
        ReplayRequested?.Invoke();
    }
    partial void OnSelectedLanguageChanged(LanguageChoice value)
    {
        foreach (var key in new[] { nameof(Text), nameof(Caption), nameof(NextLabel), nameof(DoneTitle), nameof(FontFamily) }) OnPropertyChanged(key);
        if (_errorKey is { } errorKey) Error = Text[errorKey];
        if (!_loadingLanguage) PendingPreferenceWrite = PersistLanguageAsync();
    }
    partial void OnCaptionKeyChanged(string value) => OnPropertyChanged(nameof(Caption));
    partial void OnCurrentPageChanged(int value) { OnPropertyChanged(nameof(CanBack)); OnPropertyChanged(nameof(NextLabel)); }
    partial void OnWasSkippedChanged(bool value) => OnPropertyChanged(nameof(DoneTitle));
    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanNavigate));
    partial void OnIsTransitioningChanged(bool value) => OnPropertyChanged(nameof(CanNavigate));
    private async Task PersistLanguageAsync()
    {
        await _writes.WaitAsync();
        try
        {
            var next = await store.UpdateAsync(latest => latest with { Language = SelectedLanguage.Code }); PreferencesSaved?.Invoke(next); SetError(null);
        }
        catch { SetError("languageSaveError"); }
        finally { _writes.Release(); }
    }
    [RelayCommand] private async Task GoToAsync(string page)
    {
        if (!int.TryParse(page, out var target) || target is < 0 or > 2 || !CanNavigate || IsDone || !IsOpen || target == CurrentPage) return;
        var previous = CurrentPage;
        IsTransitioning = true; CurrentPage = target;
        try { if (TransitionRequested is { } transition) await transition(previous, target); }
        finally { IsTransitioning = false; }
    }
    [RelayCommand] private async Task NextAsync()
    { if (!CanNavigate) return; if (CurrentPage == 2) await FinishAsync(false); else await GoToAsync((CurrentPage + 1).ToString()); }
    [RelayCommand] private Task PreviousAsync() => GoToAsync((CurrentPage - 1).ToString());
    [RelayCommand] private Task SkipAsync() => FinishAsync(true);
    private async Task FinishAsync(bool skipped)
    {
        if (!CanNavigate || !IsOpen) return;
        IsSaving = true; await _writes.WaitAsync();
        try
        {
            var next = await store.UpdateAsync(latest => latest with { Language = SelectedLanguage.Code, OnboardingHandled = true, OnboardingVersion = 2,
                OnboardingOutcome = IsRewatch && latest.OnboardingHandled ? latest.OnboardingOutcome : skipped ? "skipped" : "completed" }); PreferencesSaved?.Invoke(next); WasSkipped = skipped; IsDone = true; SetError(null);
        }
        catch { SetError("onboardingSaveError"); }
        finally { _writes.Release(); IsSaving = false; }
    }
    [RelayCommand] private void Dismiss() { if (IsDone) IsOpen = false; }
    [RelayCommand] private void Rewatch() { Open(true); }
    [RelayCommand] private void Replay() { if (IsOpen && !IsDone && CurrentPage == 1) ReplayRequested?.Invoke(); }
    private void SetError(string? key) { _errorKey = key; Error = key is null ? "" : Text[key]; }
}
