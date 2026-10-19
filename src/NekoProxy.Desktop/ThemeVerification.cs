using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using NekoProxy.Desktop.Controls;
using NekoProxy.Desktop.ViewModels;
namespace NekoProxy.Desktop;

// Existing opt-in theme capture, extended for selective rollback regression.
// Isolated local files only. Render images are not OS backdrop screenshots.
internal static class ThemeVerification
{
    public static async Task RecordAsync(MainWindow window,MainViewModel main,string target,Action<string,double> capture)
    {
        if(!Program.Arguments.Contains("--data-dir")||!Program.Arguments.Contains("--verification-title"))throw new InvalidOperationException("Isolated verification workspace required.");
        Directory.CreateDirectory(target);
        await main.Onboarding.SkipCommand.ExecuteAsync(null);main.Onboarding.DismissCommand.Execute(null);
        main.Page="设置";main.Settings.CategoryIndex=0;main.Settings.ReduceMotion=true;main.Settings.SimplifiedVisuals=false;main.Settings.ThemeIndex=1;
        await Task.Delay(850);main.Settings.ReduceMotion=false;await Task.Delay(100);
        var ink=window.GetVisualDescendants().OfType<TextBlock>().First(t=>t.Text=="NekoProxy"&&t.Name!="TitlebarText");
        var checks=new List<string>();var samples=new List<object>();var colors=new HashSet<string>();
        void Check(bool result,string label){if(!result)throw new InvalidOperationException(label);checks.Add(label);}
        Color Brush(string key)=>((ISolidColorBrush)Application.Current!.Resources[key]!).Color;
        async Task Snap(string name){await Task.Delay(450);capture(Path.Combine(target,name+"-render.png"),1);}
        Check(Brush("TextBrush")==Color.Parse("#1D2938"),"Original light text endpoint");
        var watch=Stopwatch.StartNew();main.Settings.ThemeIndex=2;
        for(var i=0;i<24;i++)
        {
            await Task.Delay(20);var value=((ISolidColorBrush)ink.Foreground!).Color;colors.Add(value.ToString());
            samples.Add(new{ElapsedMilliseconds=watch.Elapsed.TotalMilliseconds,Text=value.ToString(),Panel=Brush("PanelBrush").ToString(),Rim=((Color)Application.Current!.Resources["RimTop"]!).ToString(),window.ThemeMotionRunning});
        }
        Check(colors.Count>=5,"Shared theme interpolates actual text colors");
        Check(Brush("TextBrush")==Color.Parse("#EDF2F8"),"Original dark text endpoint");
        Check(!window.ThemeMotionRunning,"Theme clock stops after transition");
        Check(((Color)Application.Current!.Resources["RimTop"]!)==Color.Parse("#827F94AE"),"Original dark outer reflection endpoint");
        for(var i=0;i<10;i++)
        {
            var before=Brush("TextBrush");main.Settings.ThemeIndex=i%2==0?1:2;
            Check(before==Brush("TextBrush"),$"Rapid reversal {i+1} retains displayed color");await Task.Delay(45);
        }
        await Task.Delay(450);Check(!window.ThemeMotionRunning&&Brush("TextBrush")==Color.Parse("#EDF2F8"),"Rapid reversal settles at original dark endpoint");
        var surface=window.GetVisualDescendants().OfType<GlassSurface>().Single();
        var material=(ExperimentalAcrylicMaterial)surface.Material!;
        Check(material.TintColor==Color.Parse("#111B29")&&Math.Abs(material.TintOpacity-.72)<.001&&Math.Abs(material.MaterialOpacity-.76)<.001,"Original dark tint with retained transmission density");
        await Snap("dark-settings");
        main.Page="隐私";await main.Privacy!.RefreshAsync();main.Privacy.CategoryIndex=1;await Snap("dark-protection");
        var note=window.GetVisualDescendants().OfType<Border>().Single(b=>b.Child is TextBlock t&&t.Text=="Windows DPAPI · CurrentUser");
        Check(note.Background is ISolidColorBrush b&&b.Color==Brush("PrivacyProtectionBrush")&&b.Color.R<100,"DPAPI background fix retained without new control styling");
        main.Privacy.CategoryIndex=3;await Snap("dark-export-list");
        var list=window.GetVisualDescendants().OfType<ListBox>().Single(l=>ReferenceEquals(l.ItemsSource,main.Privacy.Exports));
        Check(list.Background is ISolidColorBrush l&&l.Color==Brush("PrivacyExportBrush"),"Export list background is theme-aware");
        main.Privacy.CategoryIndex=2;await Snap("dark-backup");
        main.Page="部署代理";await Snap("dark-wizard");
        main.Page="管理服务";await Snap("dark-home");main.AddServerCommand.Execute(null);await Snap("dark-dialog");main.CancelEditorCommand.Execute(null);
        main.Page="设置";main.Settings.ReduceMotion=true;main.Settings.ThemeIndex=1;
        Check(!window.ThemeMotionRunning&&Brush("TextBrush")==Color.Parse("#1D2938"),"Reduced motion remains immediate");
        main.Settings.SimplifiedVisuals=true;await Snap("light-simple");
        Check(material.TintOpacity==1&&material.MaterialOpacity==1,"Simplified material remains opaque");
        main.Settings.ReduceMotion=false;main.Settings.SimplifiedVisuals=false;await Snap("light-settings");
        Check(((Color)Application.Current!.Resources["RimTop"]!)==Color.Parse("#F2FFFFFF"),"Original light outer reflection endpoint");
        main.Page="隐私";main.Privacy.CategoryIndex=1;await Snap("light-protection");
        Check(Brush("PrivacyProtectionBrush")==Color.Parse("#EEF5FF")&&Brush("PrivacyExportBrush")==Color.Parse("#FAFCFF")&&Brush("PrivacyEmptyBrush")==Color.Parse("#F8FBFF"),"Original light privacy fills preserved");
        window.Width=900;window.Height=600;await Snap("light-narrow");main.Settings.ThemeIndex=2;await Snap("dark-narrow");
        main.Settings.ThemeIndex=0;await Task.Delay(450);Check(window.RequestedThemeVariant==ThemeVariant.Default,"System theme selection remains supported");
        await File.WriteAllTextAsync(Path.Combine(target,"rollback-theme-verification.json"),JsonSerializer.Serialize(new{Version="0.5.2 selective rollback",Baseline="c7dc377",NativeAvalonia=true,OperatingSystemScreenshot=false,NoNetworkActions=true,ThemeMilliseconds=ThemePaletteMotion.DurationMilliseconds,NativeScale=window.RenderScaling,Checks=checks,DistinctTextColors=colors.ToArray(),Samples=samples,SystemTheme=window.PlatformSettings?.GetColorValues().ThemeVariant.ToString()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
