using System.Diagnostics;
using System.Text.Json;
using NekoProxy.Core;
using NekoProxy.Desktop.Controls;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;
using Avalonia.Controls;
namespace NekoProxy.Desktop;

internal static class RefinementVerification
{
    public static async Task RecordAsync(MainWindow window,MainViewModel main,string directory,Action<string,double> capture)
    {
        if(!Program.Arguments.Contains("--data-dir"))throw new InvalidOperationException("Isolated data required.");
        Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.Combine(directory,"frames"));
        await main.Onboarding.SkipCommand.ExecuteAsync(null);main.Onboarding.DismissCommand.Execute(null);
        window.Width=1060;window.Height=720;
        main.Settings.ExportDirectory=Path.GetFullPath(Path.Combine(directory,"local-exports"));await main.Settings.SaveCommand.ExecuteAsync(null);
        var languages=new List<object>();
        foreach(var language in TranslationCatalog.Languages)
        {
            main.Settings.SelectedLanguage=language;await main.FlushPreferencesAsync();
            main.Page="部署代理";main.Deployment.Step=1;await Task.Delay(280);capture(Path.Combine(directory,$"deployment-{language.Code}.png"),1);
            await main.NavigateCommand.ExecuteAsync("隐私");
            foreach(var category in new[]{2,3}){main.Privacy!.CategoryIndex=category;await Task.Delay(280);capture(Path.Combine(directory,$"privacy-{category}-{language.Code}.png"),1);}
            languages.Add(new{language.Code,NativeDpi=window.RenderScaling,Width=window.Bounds.Width,Height=window.Bounds.Height});
        }
        main.Settings.SelectedLanguage=TranslationCatalog.Languages[0];await main.FlushPreferencesAsync();
        window.Width=960;window.Height=640;main.Page="部署代理";await Task.Delay(280);capture(Path.Combine(directory,"compact-deployment.png"),1);
        main.Page="隐私";foreach(var category in new[]{2,3}){main.Privacy!.CategoryIndex=category;await Task.Delay(280);capture(Path.Combine(directory,$"compact-privacy-{category}.png"),1);}
        window.Width=1060;window.Height=720;
        var frames=new List<object>();var clock=Stopwatch.StartNew();var index=0;var deck=window.FindControl<PageDeck>("MainPages")!;
        foreach(var page in new[]{"部署代理","隐私","隐私","隐私","管理服务","设置"})
        {
            main.Page=page;if(page=="隐私")main.Privacy!.CategoryIndex=(main.Privacy.CategoryIndex+1)%4;
            var segment=Stopwatch.StartNew();
            while(segment.ElapsedMilliseconds<700)
            {capture(Path.Combine(directory,"frames",$"frame-{index:D4}.png"),1);frames.Add(new{Index=index++,Milliseconds=clock.Elapsed.TotalMilliseconds,Page=page,Category=main.Privacy!.CategoryIndex,MainAnimating=deck.TransitionRunning});await Task.Delay(25);}
        }
        var workflows=new List<object>();main.PickServerFileAsync=()=>Task.FromResult<string?>(Path.GetFullPath(Path.Combine(directory,"local-server.json")));
        foreach(var protocol in new[]{0,1,2,3})
        {
            var serverFile=Path.GetFullPath(Path.Combine(directory,$"local-server-{protocol}.json"));main.PickServerFileAsync=()=>Task.FromResult<string?>(serverFile);
            var d=main.Deployment;d.ResetCommand.Execute(null);d.ProtocolIndex=protocol;d.Host="192.0.2.11";d.ServerName="LOCAL TEST "+protocol;d.NodeName="test-node-"+protocol;d.Sni="example.com";d.CertificatePath="/etc/neko/cert.pem";d.CertificateKeyPath="/etc/neko/key.pem";
            if(d.IsReality)d.GenerateKeysCommand.Execute(null);else d.GenerateCredentialCommand.Execute(null);
            main.Page="部署代理";d.Step=3;await Task.Delay(280);capture(Path.Combine(directory,$"parameters-{protocol}.png"),1);
            d.NextCommand.Execute(null);if(d.Step!=4)throw new InvalidOperationException("Parameter validation failed.");
            await Task.Delay(280);capture(Path.Combine(directory,$"preview-{protocol}.png"),1);var draft=d.BuildLocalDraft();
            await d.SaveLocalNodeCommand.ExecuteAsync(null);if(main.SelectedNode?.Id!=draft.Node.Id)throw new InvalidOperationException("Local save failed.");
            main.Page="管理服务";await main.EditNodeCommand.ExecuteAsync(null);var editor=main.NodeEditor!;
            var restored=editor.Credential==draft.Secrets.Credential&&editor.RealityPrivateKey==(draft.Secrets.RealityPrivateKey??"");
            await Task.Delay(280);capture(Path.Combine(directory,$"reopen-{protocol}.png"),1);
            await editor.SaveServerFileCommand.ExecuteAsync(null);var generated=File.Exists(serverFile);
            main.CancelNodeEditorCommand.Execute(null);await main.OpenExporterCommand.ExecuteAsync(null);
            var exporter=main.Exporter!;var noAutomaticExport=exporter.SelectedClient is null;
            exporter.SelectedClient=exporter.Clients.Single(c=>c.Type==Core.ClientType.SingBox);await exporter.GenerateCommand.ExecuteAsync(null);
            await Task.Delay(280);capture(Path.Combine(directory,$"export-{protocol}.png"),1);
            workflows.Add(new{Protocol=d.Protocol.Display(),LocalSaved=true,ReopenedSecretsMatch=restored,GeneratedServerFile=generated,ExplicitClientRequired=noAutomaticExport,ClientExported=exporter.Message.StartsWith("已生成"),RemoteConnected=false});
            main.CloseExporterCommand.Execute(null);
        }
        main.Page="隐私";await main.Privacy!.RefreshAsync();main.Privacy.CategoryIndex=3;await Task.Delay(280);capture(Path.Combine(directory,"diagnostics-with-files.png"),1);
        main.Page="部署代理";main.Deployment.ResetCommand.Execute(null);await Task.Delay(280);
        await File.WriteAllTextAsync(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames));
        await File.WriteAllTextAsync(Path.Combine(directory,"verification.json"),JsonSerializer.Serialize(new{RealNativeWindow=true,NativeDpi=window.RenderScaling,Transparency=window.ActualTransparencyLevel.ToString(),Languages=languages,Workflows=workflows,RemoteConnections=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
