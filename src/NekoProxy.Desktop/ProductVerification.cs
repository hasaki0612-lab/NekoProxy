using System.Text.Json;
using Avalonia.Controls;
using NekoProxy.Core;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;
namespace NekoProxy.Desktop;
internal static class ProductVerification
{
    public static async Task RecordAsync(MainWindow window,MainViewModel main,string directory,Action<string,double> capture)
    {
        if(!Program.Arguments.Contains("--data-dir"))throw new InvalidOperationException();
        Directory.CreateDirectory(directory);await main.Onboarding.SkipCommand.ExecuteAsync(null);main.Onboarding.DismissCommand.Execute(null);
        window.Width=1060;window.Height=720;main.Settings.SelectedLanguage=TranslationCatalog.Languages[0];await main.FlushPreferencesAsync();main.Deployment.LocalOnly=false;
        main.Page="部署代理";main.Deployment.Step=1;await Task.Delay(400);capture(Path.Combine(directory,"deployment.png"),1);
        main.Deployment.Step=2;await Task.Delay(280);capture(Path.Combine(directory,"ssh-connection.png"),1);
        await main.NavigateCommand.ExecuteAsync("隐私");foreach(var category in new[]{1,2,3}){main.Privacy!.CategoryIndex=category;await Task.Delay(280);capture(Path.Combine(directory,$"privacy-{category}.png"),1);}
        var keyIndex=Array.IndexOf(Program.Arguments,"--local-ssh-fixture");var confirmed=false;
        if(keyIndex>=0)
        {
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(Program.Arguments[keyIndex+1]));var info=doc.RootElement;
            if(info.GetProperty("host").GetString()!="127.0.0.1")throw new InvalidOperationException("UI SSH verification accepts localhost only.");
            var d=main.Deployment;d.Host="127.0.0.1";d.SshPort=info.GetProperty("port").GetInt32().ToString();d.Username=info.GetProperty("username").GetString()!;d.PrivateKeyPath=info.GetProperty("keyPath").GetString()!;d.ServerName="LOCAL SSH TEST";main.Page="部署代理";d.Step=2;
            var connection=d.ConnectCommand.ExecuteAsync(null);
            for(var attempt=0;attempt<100&&!main.HostTrust.IsOpen;attempt++)await Task.Delay(50);
            if(!main.HostTrust.IsOpen)throw new InvalidOperationException("Expected host key confirmation from actual SSH handshake.");
            confirmed=main.HostTrust.Fingerprint==info.GetProperty("fingerprint").GetString();await Task.Delay(200);capture(Path.Combine(directory,"real-ssh-fingerprint.png"),1);
            // Read and cancel. No fingerprint or fake Linux inventory is approved.
            main.HostTrust.CancelCommand.Execute(null);await connection;
            await Task.Delay(250);capture(Path.Combine(directory,"ssh-cancelled.png"),1);
        }
        main.Deployment.ResetCommand.Execute(null);main.Page="部署代理";
        foreach(var language in TranslationCatalog.Languages)
        {
            main.Settings.SelectedLanguage=language;await main.FlushPreferencesAsync();main.Deployment.Step=2;await Task.Delay(230);capture(Path.Combine(directory,$"ssh-{language.Code}.png"),1);
            main.Page="隐私";main.Privacy!.CategoryIndex=2;await Task.Delay(230);capture(Path.Combine(directory,$"backup-{language.Code}.png"),1);main.Page="部署代理";
        }
        main.Settings.SelectedLanguage=TranslationCatalog.Languages[0];await main.FlushPreferencesAsync();main.Deployment.Step=1;main.Page="部署代理";await Task.Delay(250);
        window.WindowState=WindowState.Maximized;await Task.Delay(300);capture(Path.Combine(directory,"maximized.png"),1);var maximized=window.WindowState==WindowState.Maximized;
        window.WindowState=WindowState.Normal;window.Width=1060;window.Height=720;await Task.Delay(300);capture(Path.Combine(directory,"restored.png"),1);
        await File.WriteAllTextAsync(Path.Combine(directory,"product-verification.json"),JsonSerializer.Serialize(new{ActualNativeWindow=true,window.RenderScaling,Transparency=window.ActualTransparencyLevel.ToString(),window.ExtendClientAreaToDecorationsHint,Chrome=window.ExtendClientAreaChromeHints.ToString(),Maximized=maximized,Restored=window.WindowState==WindowState.Normal,HostKeyFromRealLocalSsh=confirmed,HostKeyCancelled=true,VpsConnections=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
