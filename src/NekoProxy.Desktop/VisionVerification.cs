using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NekoProxy.Desktop.Controls;
using NekoProxy.Core;
using NekoProxy.Desktop.Onboarding;
using NekoProxy.Desktop.ViewModels;
using NekoProxy.Infrastructure;
namespace NekoProxy.Desktop;

// Explicit opt-in QA on a separate data root. No hosts, credentials or fingerprints
// are compiled into the app. Existing trust approval is required before this runs.
internal static class VisionVerification
{
    public static async Task RecordAsync(MainWindow window,MainViewModel main,string target,Action<string,double> capture)
    {
        var dataIndex=Array.IndexOf(Program.Arguments,"--data-dir");var secretIndex=Array.IndexOf(Program.Arguments,"--session-secret-file");
        if(dataIndex<0||secretIndex<0||!Program.Arguments.Contains("--verification-title"))throw new InvalidOperationException("Live verification requires an isolated data directory and explicit session-secret file.");
        var root=Path.GetFullPath(Program.Arguments[dataIndex+1]);var normal=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NekoProxy");
        if(root.Equals(normal,StringComparison.OrdinalIgnoreCase)||main.Servers.Count!=1)throw new InvalidOperationException("Use a separate single-server verification workspace.");
        var server=main.Servers.Single();var settings=await new JsonSettingsStore(root).LoadAsync();
        if(!settings.KnownHosts.ContainsKey(SshConnectionFactory.Endpoint(server)))throw new InvalidOperationException("An already approved host fingerprint is required.");
        var bytes=new WindowsSecretProtector().Unprotect(await File.ReadAllBytesAsync(Program.Arguments[secretIndex+1]));
        try{main.SshSecrets.SetSessionSecret(server,Encoding.UTF8.GetString(bytes));}finally{CryptographicOperations.ZeroMemory(bytes);}
        Directory.CreateDirectory(target);var frames=Path.Combine(target,"frames");Directory.CreateDirectory(frames);
        await main.Onboarding.SkipCommand.ExecuteAsync(null);main.Onboarding.DismissCommand.Execute(null);window.Width=1060;window.Height=720;
        main.Page="管理服务";main.SelectedServer=server;main.SelectedNode=server.Nodes.First(n=>n.Protocol==ProxyProtocol.TrojanTls);main.DetailIndex=1;
        await main.Monitoring.SelectAsync(server,main.SelectedNode,true);main.Monitoring.IntervalIndex=0;await main.Monitoring.StartCommand.ExecuteAsync(null);
        var initial=main.Monitoring.Samples.Count;var clock=Stopwatch.StartNew();
        while(main.Monitoring.Samples.Count<initial+4&&clock.Elapsed<TimeSpan.FromSeconds(65))await Task.Delay(250);
        main.Monitoring.Stop();var actual=main.Monitoring.Samples.Count(s=>s.Timestamp>DateTimeOffset.UtcNow.AddMinutes(-2)&&s.Snapshot.Success);
        if(actual<3)throw new InvalidOperationException("Insufficient real samples; no synthetic replacements are allowed.");
        await Task.Delay(400);capture(Path.Combine(target,"monitor-real.png"),1);
        var chart=window.GetVisualDescendants().OfType<TimeSeriesChart>().Single();
        // Exercise the previously crashing path: a requested tooltip must move
        // across metric/viewport transitions during real composition passes.
        chart.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Left});
        var records=new List<object>();clock.Restart();var index=0;
        while(clock.ElapsedMilliseconds<5500)
        {
            var ms=clock.ElapsedMilliseconds;
            main.Monitoring.MetricIndex=ms<1000?0:ms<2400?2:ms<3900?1:0;
            main.Monitoring.RangeIndex=ms<1800?0:ms<3600?1:0;
            capture(Path.Combine(frames,$"frame-{index:D4}.png"),1);records.Add(new{Index=index++,WallMilliseconds=clock.Elapsed.TotalMilliseconds});await Task.Delay(35);
        }
        await File.WriteAllTextAsync(Path.Combine(target,"frames.json"),JsonSerializer.Serialize(records));
        main.Settings.ReduceMotion=true;main.Monitoring.MetricIndex=2;await Task.Delay(300);capture(Path.Combine(target,"reduced-motion-tooltip.png"),1);main.Settings.ReduceMotion=false;
        // A real pause creates the missing interval; no samples are inserted to draw a gap.
        await Task.Delay(21000);await main.Monitoring.RefreshAsync();await Task.Delay(350);capture(Path.Combine(target,"monitor-gap.png"),1);
        main.Page="高级功能";main.Advanced.CategoryIndex=0;main.Advanced.RequestServiceCommand.Execute("CheckConfiguration");while(main.Advanced.IsBusy)await Task.Delay(50);await Task.Delay(300);capture(Path.Combine(target,"advanced-config.png"),1);
        main.Advanced.CategoryIndex=2;await main.Advanced.ConnectTerminalCommand.ExecuteAsync(null);
        // Only a read-only, task-specific command is issued in this explicitly authorized QA path.
        main.Advanced.TerminalInput="printf 'NekoProxy SSH verification\\n'; uname -srm";await main.Advanced.SendCommand.ExecuteAsync(null);await Task.Delay(1500);capture(Path.Combine(target,"terminal-real.png"),1);await main.Advanced.DisconnectAsync();
        main.Page="部署代理";main.Deployment.Step=1;await Task.Delay(350);capture(Path.Combine(target,"deployment-after.png"),1);
        main.Deployment.ProtocolIndex=2;main.Deployment.Step=3;main.Deployment.AutomaticCertificate=true;await Task.Delay(300);capture(Path.Combine(target,"automatic-certificate.png"),1);
        await main.NavigateCommand.ExecuteAsync("隐私");foreach(var category in new[]{2,3}){main.Privacy!.CategoryIndex=category;await Task.Delay(300);capture(Path.Combine(target,$"privacy-{category}-after.png"),1);}
        main.Page="设置";main.Settings.CategoryIndex=0;await Task.Delay(300);capture(Path.Combine(target,"settings-after.png"),1);
        main.Settings.SimplifiedVisuals=true;await Task.Delay(300);capture(Path.Combine(target,"settings-simple-fallback.png"),1);main.Settings.SimplifiedVisuals=false;
        foreach(var language in TranslationCatalog.Languages)
        {
            main.Settings.SelectedLanguage=language;await main.FlushPreferencesAsync();main.Page="管理服务";main.DetailIndex=1;await Task.Delay(280);capture(Path.Combine(target,$"monitor-{language.Code}.png"),1);
            main.Page="高级功能";main.Advanced.CategoryIndex=1;await Task.Delay(260);capture(Path.Combine(target,$"tools-{language.Code}.png"),1);
        }
        main.Settings.SelectedLanguage=TranslationCatalog.Languages[0];await main.FlushPreferencesAsync();main.Page="管理服务";main.DetailIndex=1;
        await File.WriteAllTextAsync(Path.Combine(target,"vision-verification.json"),JsonSerializer.Serialize(new{Version=AppVersion.Current,ActualNativeWindow=true,ActualSamples=actual,window.RenderScaling,Transparency=window.ActualTransparencyLevel.ToString(),Clock=DateTimeOffset.UtcNow,NativeChart=true,HtmlOrWebView=false,PauseGapSeconds=21,SyntheticSamples=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
