using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Infrastructure;
using Xunit;
namespace NekoProxy.Tests;
public sealed class OnboardingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NekoProxy-onboarding-tests", Guid.NewGuid().ToString("N"));
    private readonly JsonSettingsStore _store;
    public OnboardingTests() { _store = new(_directory); }
    private OnboardingViewModel Create() => new(_store, new TranslationCatalog(), new TestMotion());
    [Fact] public async Task OldV01SettingsKeepValuesAndOpenOnboarding()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{\"Language\":\"简体中文\",\"SshTimeoutSeconds\":37,\"ExportDirectory\":\"C:\\\\exports\"}");
        var saved = await _store.LoadAsync(); var vm = Create(); vm.Initialize(saved);
        Assert.True(vm.IsOpen); Assert.False(vm.IsRewatch); Assert.Equal("zh-Hans", vm.SelectedLanguage.Code); Assert.Equal(37, saved.SshTimeoutSeconds);
    }
    [Theory][InlineData(false, "completed")][InlineData(true, "skipped")]
    public async Task CompletionAndSkipSurviveRestart(bool skip, string expected)
    {
        var vm = Create(); vm.Initialize(await _store.LoadAsync());
        if (skip) await vm.SkipCommand.ExecuteAsync(null);
        else { await vm.NextCommand.ExecuteAsync(null); await vm.NextCommand.ExecuteAsync(null); await vm.NextCommand.ExecuteAsync(null); }
        Assert.True(vm.IsDone); var settings = await _store.LoadAsync(); Assert.True(settings.OnboardingHandled); Assert.Equal(expected, settings.OnboardingOutcome);
        var restarted = Create(); restarted.Initialize(settings); Assert.False(restarted.IsOpen);
    }
    [Fact] public async Task RewatchDoesNotAlterServerFileOrCompletionFlag()
    {
        Directory.CreateDirectory(_directory); var path = Path.Combine(_directory, "servers.json"); await File.WriteAllTextAsync(path, "existing server metadata");
        await _store.SaveAsync(new() { OnboardingHandled = true, Language = "ru" });
        var vm = Create(); vm.Initialize(await _store.LoadAsync()); vm.RewatchCommand.Execute(null);
        Assert.True(vm.IsRewatch); Assert.True(vm.IsOpen); Assert.Equal(0, vm.CurrentPage); Assert.True((await _store.LoadAsync()).OnboardingHandled); Assert.Equal("existing server metadata", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task SkippingManualRewatchPreservesOriginalCompletionOutcome()
    {
        await _store.SaveAsync(new() { OnboardingHandled = true, OnboardingOutcome = "completed", OnboardingVersion = 2 });
        var vm = Create(); vm.Initialize(await _store.LoadAsync()); vm.RewatchCommand.Execute(null); await vm.SkipCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone); Assert.Equal("completed", (await _store.LoadAsync()).OnboardingOutcome);
    }
    [Theory][InlineData("zh-Hans")][InlineData("zh-Hant")][InlineData("en")][InlineData("ja")][InlineData("yue-Hant")][InlineData("ru")]
    public async Task EachLanguageUpdatesAllPagesCaptionsAndPersists(string code)
    {
        var vm = Create(); vm.Initialize(await _store.LoadAsync()); vm.SelectedLanguage = vm.Languages.Single(x => x.Code == code); await vm.PendingPreferenceWrite;
        Assert.Equal(code, vm.SelectedLanguage.Code); Assert.NotEmpty(vm.Text["welcomeTitle"]); Assert.NotEmpty(vm.Text["privacyTitle"]); Assert.NotEmpty(vm.Text["replay"]); Assert.NotEmpty(vm.Text["captions.ready"]);
        // The default legacy name is normalized without unnecessary writes.
        Assert.Equal(code, TranslationCatalog.Normalize((await _store.LoadAsync()).Language));
    }
    [Fact] public void HantUsesFemininePronounEverywhere()
    { var text = new TranslationCatalog().For("zh-Hant"); Assert.Contains("妳", text["welcomeTitle"]); Assert.Contains("妳", text["privacyTitle"]); Assert.DoesNotContain(text.Values, v => v.Contains('你')); }
    [Fact] public void NativeLanguageNamesAndFallbackAreStable()
    {
        Assert.Equal(new[] { "简体中文", "繁體中文", "English", "日本語", "廣東話", "Русский" }, TranslationCatalog.Languages.Select(x => x.Name));
        var catalog = new TranslationCatalog(); Assert.Equal(catalog.Get("zh-Hans", "next"), catalog.Get("unknown", "next")); Assert.Equal("[missing]", catalog.Get("en", "missing"));
    }
    [Fact] public async Task RapidLanguageSelectionKeepsLatestPreferenceAndOtherValues()
    {
        await _store.SaveAsync(new() { SshTimeoutSeconds = 49, ExportDirectory = _directory, OnboardingHandled = true });
        var vm = Create(); vm.Initialize(await _store.LoadAsync());
        foreach (var language in vm.Languages.Reverse()) vm.SelectedLanguage = language;
        await vm.PendingPreferenceWrite; var stored = await _store.LoadAsync(); Assert.Equal(vm.SelectedLanguage.Code, stored.Language); Assert.Equal(49, stored.SshTimeoutSeconds); Assert.True(stored.OnboardingHandled);
    }
    [Fact] public async Task NavigationIsGuardedDuringTransition()
    {
        var vm = Create(); vm.Initialize(new()); var completion = new TaskCompletionSource(); vm.TransitionRequested += (_, _) => completion.Task;
        var first = vm.NextCommand.ExecuteAsync(null); Assert.True(vm.IsTransitioning);
        await vm.GoToCommand.ExecuteAsync("2"); Assert.Equal(1, vm.CurrentPage); completion.SetResult(); await first; Assert.False(vm.IsTransitioning);
    }
    [Fact] public async Task SaveFailureDoesNotClaimOnboardingCompleted()
    {
        Directory.CreateDirectory(_directory); await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "corrupt");
        var vm = Create(); vm.Initialize(new()); await vm.SkipCommand.ExecuteAsync(null); Assert.False(vm.IsDone); Assert.NotEmpty(vm.Error);
        Assert.Equal("corrupt", await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json")));
    }
    [Fact] public async Task FailureMessagesAlsoFollowSelectedLanguage()
    {
        Directory.CreateDirectory(_directory); await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "corrupt");
        var vm = Create(); vm.Initialize(new() { Language = "en" }); await vm.SkipCommand.ExecuteAsync(null);
        Assert.Equal(vm.Text["onboardingSaveError"], vm.Error); Assert.Contains("Could not", vm.Error);
        vm.SelectedLanguage = vm.Languages.Single(x => x.Code == "ja"); await vm.PendingPreferenceWrite;
        Assert.Equal(vm.Text["languageSaveError"], vm.Error);
    }
    [Fact] public async Task SettingsSavePreservesLatestOnboardingLanguage()
    {
        var settings = new SettingsViewModel(_store, new TestPlatform()); settings.Load(new());
        var vm = Create(); vm.Initialize(new()); vm.SelectedLanguage = vm.Languages.Single(x => x.Code == "ja"); await vm.PendingPreferenceWrite;
        await vm.SkipCommand.ExecuteAsync(null); settings.ExportDirectory = _directory; settings.Timeout = "28"; await settings.SaveCommand.ExecuteAsync(null);
        var saved = await _store.LoadAsync(); Assert.Equal("ja", saved.Language); Assert.True(saved.OnboardingHandled); Assert.Equal(28, saved.SshTimeoutSeconds);
    }
    [Fact] public void SystemAndApplicationMotionPreferencesBothApply()
    {
        var vm = new OnboardingViewModel(_store, new TranslationCatalog(), new TestMotion { SystemReducesMotion = true }); vm.Initialize(new()); Assert.True(vm.ReduceMotion);
        var local = Create(); local.Initialize(new() { ReduceMotion = true }); Assert.True(local.ReduceMotion);
    }
    [Fact] public void LinesGrowInOrderInsteadOfAppearingAtOnce()
    {
        var start = OnboardingMotion.Evaluate(260); Assert.Equal(0, start.FirstGrowth);
        var growing = OnboardingMotion.Evaluate(600); Assert.InRange(growing.FirstGrowth, .01, .99); Assert.Equal(0, growing.SecondGrowth);
        var ssh = OnboardingMotion.Evaluate(1040); Assert.Equal(1, ssh.FirstGrowth); Assert.Equal(0, ssh.SecondGrowth);
        Assert.Equal(1, OnboardingMotion.Evaluate(1720).SecondGrowth);
    }
    [Fact] public void SplitIsContinuousAndLinesRemainUnderRingEdges()
    {
        var before = OnboardingMotion.Evaluate(1900); var during = OnboardingMotion.Evaluate(2200); var after = OnboardingMotion.Evaluate(2460);
        Assert.Equal(0, before.Offset); Assert.InRange(during.Offset, .01, 10.99); Assert.Equal(11, after.Offset);
        Assert.Equal(46 * 46, during.RingIntersection * during.RingIntersection + during.Offset * during.Offset, 7);
        Assert.InRange(OnboardingMotion.Evaluate(2199).Offset - during.Offset, -.1, 0);
    }
    [Fact] public void DotsHaveContinuousOppositeMotionAndAreHiddenInsideSsh()
    {
        var first = OnboardingMotion.Evaluate(3500); var next = OnboardingMotion.Evaluate(3510);
        Assert.True(next.Dots.First(d => d.Direction == 1).X > first.Dots.First(d => d.Direction == 1).X);
        Assert.True(next.Dots.First(d => d.Direction == -1).X < first.Dots.First(d => d.Direction == -1).X);
        for (var t = 2350; t < 8000; t += 20)
        {
            var frame = OnboardingMotion.Evaluate(t);
            Assert.All(frame.Dots, dot => { Assert.True(Math.Abs(dot.X - 400) >= frame.RingIntersection); Assert.InRange(dot.Opacity, 0, 1); });
            Assert.True(frame.Dots.Count <= 10);
        }
    }
    [Fact] public void ReducedMotionHasCompleteGeometryAndNoDataLoop()
    { var frame = OnboardingMotion.Evaluate(0, true); Assert.Equal(1, frame.FirstGrowth); Assert.Equal(1, frame.SecondGrowth); Assert.Equal(11, frame.Offset); Assert.Empty(frame.Dots); Assert.Equal("captions.ready", frame.CaptionKey); }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class TestMotion : IMotionPreferences { public bool SystemReducesMotion { get; init; } }
    private sealed class TestPlatform : IPlatformActions { public Task CopyAsync(string text) => Task.CompletedTask; public Task OpenDirectoryAsync(string directory) => Task.CompletedTask; }
}
