using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using NekoProxy.Desktop.Controls;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;
using Avalonia.VisualTree;
namespace NekoProxy.Desktop;

internal static class InteractionVerification
{
    public static async Task RecordAsync(MainWindow window, MainViewModel main, string directory, Action<string,double> capture)
    {
        if(!Program.Arguments.Contains("--data-dir")) throw new InvalidOperationException("An isolated data directory is required.");
        Directory.CreateDirectory(directory); Directory.CreateDirectory(Path.Combine(directory,"frames"));
        window.Width=1060; window.Height=720;
        var deck=window.FindControl<PageDeck>("MainPages")!;
        var titles=new List<object>();
        main.Onboarding.Open(true);
        foreach(var language in TranslationCatalog.Languages)
        {
            main.Onboarding.SelectedLanguage=language; await main.FlushPreferencesAsync(); await Task.Delay(250);
            var title=window.GetVisualDescendants().OfType<TextBlock>().Single(c=>c.Name=="WelcomeTitle");
            var legacy=new TextLayout(title.Text!,new Typeface(title.FontFamily,title.FontStyle,title.FontWeight),title.FontSize,title.Foreground,maxWidth:title.Bounds.Width,lineHeight:48);
            foreach(var scale in new[]{1d,1.25,1.5,2d}) capture(Path.Combine(directory,$"welcome-{language.Code}-{scale}.png"),scale);
            titles.Add(new{language.Code,title.Text,Height=title.Bounds.Height,AutomaticTextHeight=title.TextLayout.Height,LegacyFixedTextHeight=legacy.Height,NativeDpi=window.RenderScaling,Font=title.FontFamily.ToString(),RasterScalesOnly=new[]{1d,1.25,1.5,2d}});
        }
        main.Onboarding.SelectedLanguage=TranslationCatalog.Languages[0]; await main.FlushPreferencesAsync();
        await main.Onboarding.SkipCommand.ExecuteAsync(null); main.Onboarding.DismissCommand.Execute(null);
        main.Settings.ReduceMotion=false; main.Deployment.Host="203.0.113.77";
        main.Page="部署代理"; await Task.Delay(300);
        var frames=new List<object>(); var clock=Stopwatch.StartNew(); var index=0;
        foreach(var destination in new[]{"部署代理","管理服务","隐私","设置","部署代理"})
        {
            await main.NavigateCommand.ExecuteAsync(destination);
            var segment=Stopwatch.StartNew();
            while(segment.ElapsedMilliseconds<900)
            {
                capture(Path.Combine(directory,"frames",$"frame-{index:D4}.png"),1);
                frames.Add(new{Index=index++,Milliseconds=clock.Elapsed.TotalMilliseconds,Page=destination,Animating=deck.TransitionRunning,
                    Opacities=deck.Children.Select(c=>c.Opacity).ToArray(),Offsets=deck.Children.Select(c=>(c.RenderTransform as TranslateTransform)?.X??0).ToArray()});
                await Task.Delay(25);
            }
        }
        foreach(var p in new[]{"隐私","设置","管理服务","高级功能","部署代理","设置","隐私"}) { main.Page=p; await Task.Delay(18); }
        await Task.Delay(350);
        var rapid=deck.Children.Count(c=>c.IsVisible)==1 && deck.Children[3].IsVisible && !deck.TransitionRunning;
        var retained=main.Deployment.Host=="203.0.113.77";
        main.Settings.ReduceMotion=true; main.Page="设置";
        var reduced=!deck.TransitionRunning && deck.Children[4].IsVisible && deck.Children[4].Opacity==1;
        main.Settings.ReduceMotion=false;
        foreach(var language in TranslationCatalog.Languages)
        {
            main.Settings.SelectedLanguage=language; await main.FlushPreferencesAsync();
            foreach(var route in new[]{"设置","隐私"})
            {
                await main.NavigateCommand.ExecuteAsync(route);
                for(var category=0;category<(route=="设置"?4:5);category++)
                {
                    if(route=="设置")main.Settings.CategoryIndex=category; else main.Privacy!.CategoryIndex=category;
                    await Task.Delay(280); capture(Path.Combine(directory,$"{route}-{language.Code}-{category}.png"),1);
                }
            }
        }
        main.Settings.SelectedLanguage=TranslationCatalog.Languages[0]; await main.FlushPreferencesAsync();
        window.Width=960;window.Height=640;
        foreach(var route in new[]{"设置","隐私"})
        {
            await main.NavigateCommand.ExecuteAsync(route);
            for(var category=0;category<(route=="设置"?4:5);category++)
            { if(route=="设置")main.Settings.CategoryIndex=category;else main.Privacy!.CategoryIndex=category;await Task.Delay(280);capture(Path.Combine(directory,$"compact-{route}-{category}.png"),1); }
        }
        window.Width=1060;window.Height=720;
        await main.LoadExamplesCommand.ExecuteAsync(null);
        foreach(var language in TranslationCatalog.Languages)
        {
            main.Settings.SelectedLanguage=language;await main.FlushPreferencesAsync();
            main.Page="部署代理";main.Deployment.Step=3;
            foreach(var protocol in new[]{2,3})
            {
                main.Deployment.ProtocolIndex=protocol;main.Deployment.Sni="example.com";
                main.Deployment.GenerateCredentialCommand.Execute(null);
                await Task.Delay(280);capture(Path.Combine(directory,$"protocol-{language.Code}-{protocol}.png"),1);
            }
            main.Deployment.Step=4;await Task.Delay(200);capture(Path.Combine(directory,$"preview-{language.Code}.png"),1);
            main.Page="管理服务";main.AddNodeCommand.Execute(null);main.NodeEditor!.ProtocolIndex=3;
            main.NodeEditor.Name="LOCAL TEST SS2022";main.NodeEditor.GenerateCredentialCommand.Execute(null);
            await Task.Delay(250);capture(Path.Combine(directory,$"node-editor-{language.Code}.png"),1);main.CancelNodeEditorCommand.Execute(null);
        }
        main.Settings.SelectedLanguage=TranslationCatalog.Languages[0];await main.FlushPreferencesAsync();
        main.Page="设置";main.Settings.CategoryIndex=2;
        await main.Settings.Updates.CheckUpdatesCommand.ExecuteAsync(null);
        main.Settings.Updates.AutoCheck=false;await main.Settings.Updates.PendingPreferenceWrite;
        main.Settings.Updates.AutoCheck=true;await main.Settings.Updates.PendingPreferenceWrite;
        await Task.Delay(280);capture(Path.Combine(directory,"manual-update-unconfigured.png"),1);
        main.Page="隐私";main.Privacy!.CategoryIndex=4;main.Privacy.ClearCache=true;main.Privacy.RequestClearCommand.Execute(null);
        await Task.Delay(300);capture(Path.Combine(directory,"clear-confirmation.png"),1);main.Privacy.CancelConfirmationCommand.Execute(null);
        await Task.Delay(220);
        await File.WriteAllTextAsync(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames));
        await File.WriteAllTextAsync(Path.Combine(directory,"titles.json"),JsonSerializer.Serialize(titles,new JsonSerializerOptions{WriteIndented=true}));
        await File.WriteAllTextAsync(Path.Combine(directory,"interaction.json"),JsonSerializer.Serialize(new{RealWindow=true,RapidSwitch=rapid,FormRetained=retained,ReducedMotion=reduced,
            AnimatedSamples=frames.Count,NativeDpi=window.RenderScaling,Transparency=window.ActualTransparencyLevel.ToString(),PendingVpsConnections=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
