using System.Text.Json;
using Avalonia.Controls;
using NekoProxy.Desktop.Localization;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;
namespace NekoProxy.Desktop;
internal static class DesktopVerification
{
    public static async Task RecordAsync(MainWindow window, MainViewModel main, string target, Action<string,double> capture)
    {
        if (!Program.Arguments.Contains("--data-dir")) throw new InvalidOperationException("Verification needs isolated --data-dir.");
        Directory.CreateDirectory(target); await main.Onboarding.SkipCommand.ExecuteAsync(null); main.Onboarding.DismissCommand.Execute(null);
        await main.LoadExamplesCommand.ExecuteAsync(null);
        window.Width=1200; window.Height=800;
        var results=new List<object>();
        foreach(var language in TranslationCatalog.Languages)
        {
            main.Settings.SelectedLanguage=language; await main.FlushPreferencesAsync(); await Task.Delay(240);
            foreach(var page in new[]{"管理服务","部署代理","高级功能","设置","隐私"})
            {
                main.Page=page; if(page=="隐私" && main.Privacy is {} privacy)await privacy.RefreshAsync();
                await Task.Delay(240); capture(Path.Combine(target,$"{language.Code}-{Array.IndexOf(new[]{"管理服务","部署代理","高级功能","设置","隐私"},page)}.png"),1);
            }
            main.Page="部署代理"; main.Deployment.Step=2; main.Deployment.NextCommand.Execute(null); await Task.Delay(200);
            capture(Path.Combine(target,$"{language.Code}-validation.png"),1);
            results.Add(new{Language=language.Code,Shared=LocalizationHub.Current.Language==language.Code&&main.Onboarding.SelectedLanguage.Code==language.Code,DynamicMessage=LocalizationHub.Current.Translate(main.Deployment.Message)});
            main.Deployment.ResetCommand.Execute(null);
        }
        main.Settings.SelectedLanguage=TranslationCatalog.Languages[0]; await main.FlushPreferencesAsync();
        foreach(var size in new[]{(960,640),(1060,720),(1200,800)})
        {
            window.Width=size.Item1; window.Height=size.Item2;
            foreach(var page in new[]{"管理服务","部署代理","设置","隐私"})
            {
                main.Page=page; await Task.Delay(220); capture(Path.Combine(target,$"layout-{size.Item1}-{page}.png"),1);
            }
            main.Page="部署代理";
            for(var step=2;step<=5;step++){main.Deployment.Step=step; await Task.Delay(160); capture(Path.Combine(target,$"layout-{size.Item1}-step{step}.png"),1);}
        }
        window.WindowState=WindowState.Maximized; main.Page="部署代理";main.Deployment.Step=1;await Task.Delay(400);capture(Path.Combine(target,"maximized.png"),1);
        results.Add(new{Maximized=window.WindowState==WindowState.Maximized,Width=window.Bounds.Width,Height=window.Bounds.Height,NativeScale=window.RenderScaling});
        window.WindowState=WindowState.Normal; window.Width=1060; window.Height=720; main.Page="隐私";
        if(main.Privacy is {} p){p.ClearServers=true;p.RequestClearCommand.Execute(null); await Task.Delay(200);capture(Path.Combine(target,"privacy-confirmation.png"),1);p.CancelConfirmationCommand.Execute(null);}
        main.Page="管理服务";main.OpenExporterCommand.Execute(null);await Task.Delay(200);capture(Path.Combine(target,"export-dialog.png"),1);main.CloseExporterCommand.Execute(null);
        await File.WriteAllTextAsync(Path.Combine(target,"verification.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
