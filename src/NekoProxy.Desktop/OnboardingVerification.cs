using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Desktop.Views;
namespace NekoProxy.Desktop;
internal static class OnboardingVerification
{
    public static async Task CheckSizingAsync(MainWindow window, string directory, Action<string, double> capture)
    {
        Directory.CreateDirectory(directory);
        window.Width = 1280; window.Height = 720; await Task.Delay(400);
        capture(Path.Combine(directory, "normal-1280.png"), 1);
        window.WindowState = WindowState.Maximized; await Task.Delay(400);
        var maximized = window.WindowState == WindowState.Maximized;
        var maximumWidth = window.Bounds.Width; var maximumHeight = window.Bounds.Height;
        capture(Path.Combine(directory, "maximized.png"), 1);
        window.WindowState = WindowState.Normal; window.Width = 1060; window.Height = 720; await Task.Delay(400);
        capture(Path.Combine(directory, "restored-1060.png"), 1);
        await File.WriteAllTextAsync(Path.Combine(directory, "sizing.json"), JsonSerializer.Serialize(new
        { Maximized = maximized, MaximizedWidth = maximumWidth, MaximizedHeight = maximumHeight,
            Restored = window.WindowState == WindowState.Normal, RestoredWidth = window.Bounds.Width, RestoredHeight = window.Bounds.Height, NativeScale = window.RenderScaling }));
    }
    public static async Task RecordAsync(MainWindow window, MainViewModel main, OnboardingView view, string target, Action<string, double> capture)
    {
        if (!Program.Arguments.Contains("--data-dir")) throw new InvalidOperationException("Capture requires an explicit isolated --data-dir.");
        target = Path.GetFullPath(target); Directory.CreateDirectory(target);
        var framesDirectory = Path.Combine(target, "frames"); Directory.CreateDirectory(framesDirectory);
        window.Width = 1280; window.Height = 720;
        var tour = main.Onboarding; var initial = main.HasOnboarding;
        await main.LoadExamplesCommand.ExecuteAsync(null);
        var dataIndex = Array.IndexOf(Program.Arguments, "--data-dir");
        var serverFile = Path.Combine(Program.Arguments[dataIndex + 1], "servers.json");
        var originalServerBytes = await File.ReadAllBytesAsync(serverFile);
        await Task.Delay(500);
        foreach (var language in tour.Languages)
        {
            tour.SelectedLanguage = language; await tour.PendingPreferenceWrite; await Task.Delay(220);
            capture(Path.Combine(target, $"welcome-{language.Code}.png"), 1);
        }
        tour.SelectedLanguage = tour.Languages.Single(x => x.Code == "en"); await tour.PendingPreferenceWrite;
        await tour.GoToCommand.ExecuteAsync("1"); tour.ReplayCommand.Execute(null);
        var clock = Stopwatch.StartNew(); var frames = new List<object>();
        for (var index = 0; clock.ElapsedMilliseconds < 8500; index++)
        {
            var frame = view.Diagram.Frame;
            capture(Path.Combine(framesDirectory, $"frame-{index:D4}.png"), 1);
            frames.Add(new { Index = index, WallMilliseconds = clock.Elapsed.TotalMilliseconds, AnimationMilliseconds = view.Diagram.ClockMilliseconds,
                frame.FirstGrowth, frame.SecondGrowth, frame.Offset, frame.Dots });
            await Task.Delay(40);
        }
        var recordedMilliseconds = clock.Elapsed.TotalMilliseconds; clock.Stop();
        await File.WriteAllTextAsync(Path.Combine(target, "frames.json"), JsonSerializer.Serialize(frames));
        foreach (var language in tour.Languages)
        {
            tour.SelectedLanguage = language; await tour.PendingPreferenceWrite; await Task.Delay(220);
            capture(Path.Combine(target, $"ssh-{language.Code}.png"), 1);
        }
        await tour.GoToCommand.ExecuteAsync("2"); var ticksAfterLeave = view.Diagram.Ticks;
        await Task.Delay(250); var stoppedWhileHidden = !view.Diagram.IsRunning && ticksAfterLeave == view.Diagram.Ticks;
        foreach (var language in tour.Languages)
        {
            tour.SelectedLanguage = language; await tour.PendingPreferenceWrite; await Task.Delay(180);
            capture(Path.Combine(target, $"privacy-{language.Code}.png"), 1);
        }
        window.Width = 1060; window.Height = 720;
        tour.SelectedLanguage = tour.Languages.Single(x => x.Code == "ru"); await tour.PendingPreferenceWrite; await Task.Delay(300);
        capture(Path.Combine(target, "privacy-ru-1060.png"), 1);
        await tour.GoToCommand.ExecuteAsync("0"); await Task.Delay(200); capture(Path.Combine(target, "welcome-ru-1060.png"), 1);
        await tour.GoToCommand.ExecuteAsync("1"); await Task.Delay(2900); capture(Path.Combine(target, "ssh-ru-1060.png"), 1);
        capture(Path.Combine(target, "ssh-raster-125.png"), 1.25); capture(Path.Combine(target, "ssh-raster-150.png"), 1.5);
        var replayBefore = view.Diagram.ClockMilliseconds; tour.ReplayCommand.Execute(null); await Task.Delay(60);
        var replayReset = view.Diagram.ClockMilliseconds < replayBefore;
        var rapid = tour.GoToCommand.ExecuteAsync("0"); await tour.GoToCommand.ExecuteAsync("2"); await rapid;
        var guardedRapidNavigation = tour.CurrentPage == 0 && !view.Diagram.IsRunning;
        await tour.GoToCommand.ExecuteAsync("1"); await Task.Delay(300);
        window.WindowState = WindowState.Minimized; await Task.Delay(200); var pausedWhenMinimized = !view.Diagram.IsRunning;
        window.WindowState = WindowState.Normal; await Task.Delay(200); var resumedAfterRestore = view.Diagram.IsRunning;
        tour.SelectedLanguage = tour.Languages.Single(x => x.Code == "zh-Hant"); await tour.PendingPreferenceWrite;
        await tour.GoToCommand.ExecuteAsync("2"); await tour.NextCommand.ExecuteAsync(null); await Task.Delay(220);
        capture(Path.Combine(target, "done-zh-Hant.png"), 1);
        tour.DismissCommand.Execute(null); window.Width = 1280; await Task.Delay(220); capture(Path.Combine(target, "management-regression.png"), 1);
        main.Settings.RewatchCommand.Execute(null); var manualRewatch = tour.IsRewatch && tour.IsOpen && tour.CurrentPage == 0;
        main.Settings.ReduceMotion = true; await main.Settings.SaveCommand.ExecuteAsync(null);
        await tour.GoToCommand.ExecuteAsync("1"); await Task.Delay(220);
        var reducedWithoutLoop = !view.Diagram.IsRunning && view.Diagram.Frame.Dots.Count == 0 && view.Diagram.Frame.Offset == 11;
        capture(Path.Combine(target, "reduced-motion.png"), 1);
        await tour.SkipCommand.ExecuteAsync(null); tour.DismissCommand.Execute(null);
        main.Settings.ReduceMotion = false; await main.Settings.SaveCommand.ExecuteAsync(null);
        var finalServerBytes = await File.ReadAllBytesAsync(serverFile);
        var unchangedData = originalServerBytes.SequenceEqual(finalServerBytes);
        await File.WriteAllTextAsync(Path.Combine(target, "lifecycle.json"), JsonSerializer.Serialize(new
        {
            NativeWindow = true, FirstLaunchShowedOnboarding = initial, StoppedWhileHidden = stoppedWhileHidden,
            ReplayResetClock = replayReset, RapidNavigationGuarded = guardedRapidNavigation,
            PausedWhenMinimized = pausedWhenMinimized, ResumedAfterRestore = resumedAfterRestore,
            SettingsRewatch = manualRewatch, ReducedMotionWithoutLoop = reducedWithoutLoop,
            ServerFileUnchanged = unchangedData, NativeRenderScaling = window.RenderScaling,
            OperatingSystem = Environment.OSVersion.VersionString, DiagramStarts = view.Diagram.Starts, DiagramStops = view.Diagram.Stops,
            Frames = frames.Count, RecordedMilliseconds = recordedMilliseconds,
            RasterScalingOnly = new[] { 1.25, 1.5 }, OnboardingDependencies = "Settings store, translations and read-only motion preferences only"
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!(initial && stoppedWhileHidden && replayReset && guardedRapidNavigation && pausedWhenMinimized && resumedAfterRestore && manualRewatch && reducedWithoutLoop && unchangedData))
            throw new InvalidOperationException("One or more native lifecycle checks failed; inspect lifecycle.json.");
    }
}
