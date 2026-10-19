using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using NekoProxy.Desktop.ViewModels;

namespace NekoProxy.Desktop;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if((Program.Arguments.Contains("--vision-capture")||Program.Arguments.Contains("--theme-capture"))&&Program.Arguments.Contains("--data-dir"))ShowActivated=false;
        if(Program.Arguments.Contains("--verification-title")&&Program.Arguments.Contains("--data-dir"))Title="NekoProxy · 临时验收";
        if(OperatingSystem.IsMacOS())
        {
            ExtendClientAreaChromeHints=Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
            TitlebarTrafficControls.IsVisible=false;TitlebarText.IsVisible=false;
        }
        Activated += (_,_)=>Classes.Set("systemReduced",new Onboarding.MotionPreferences().SystemReducesMotion);
        Opened += async (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            await vm.InitializeAsync();
            await InteractiveVerification.PrepareAsync(vm);
            await RestoreWindowAsync(vm);
            if(Program.Arguments.Contains("--theme-capture")){var t=Array.IndexOf(Program.Arguments,"--theme-capture");var dir=Program.Arguments[t+1];try{await ThemeVerification.RecordAsync(this,vm,dir,Capture);}catch(Exception e){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"capture-error.txt"),e.ToString());Environment.ExitCode=1;}Close();return;}if(Program.Arguments.Contains("--vision-capture"))
            {
                var index=Array.IndexOf(Program.Arguments,"--vision-capture");var directory=Program.Arguments[index+1];
                try{await VisionVerification.RecordAsync(this,vm,directory,Capture);}
                catch(Exception e){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"capture-error.txt"),e.ToString());Environment.ExitCode=1;}
                if(Program.Arguments.Contains("--exit-after-capture"))Close();return;
            }
            if(Program.Arguments.Contains("--product-capture"))
            {
                var position=Array.IndexOf(Program.Arguments,"--product-capture");var directory=Program.Arguments[position+1];
                try{await ProductVerification.RecordAsync(this,vm,directory,Capture);}
                catch(Exception e){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"capture-error.txt"),e.ToString());Environment.ExitCode=1;}
                if(Program.Arguments.Contains("--exit-after-capture"))Close();return;
            }
            if(Program.Arguments.Contains("--data-dir")&&Program.Arguments.Any(a=>a.EndsWith("-capture")))vm.Deployment.LocalOnly=true;
            if(Program.Arguments.Contains("--refinement-capture"))
            {
                var position=Array.IndexOf(Program.Arguments,"--refinement-capture");var directory=Program.Arguments[position+1];
                try{await RefinementVerification.RecordAsync(this,vm,directory,Capture);}
                catch(Exception e){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"capture-error.txt"),e.ToString());Environment.ExitCode=1;}
                if(Program.Arguments.Contains("--exit-after-capture"))Close();return;
            }
            if (Program.Arguments.Contains("--interaction-capture"))
            {
                var position=Array.IndexOf(Program.Arguments,"--interaction-capture");
                try { await InteractionVerification.RecordAsync(this,vm,Program.Arguments[position+1],Capture); }
                catch(Exception e) { File.WriteAllText(Path.Combine(Program.Arguments[position+1],"capture-error.txt"),e.ToString()); }
                if(Program.Arguments.Contains("--exit-after-capture")) Close(); return;
            }
            if (Program.Arguments.Contains("--polish-capture"))
            {
                var position = Array.IndexOf(Program.Arguments, "--polish-capture");
                try { await DesktopVerification.RecordAsync(this, vm, Program.Arguments[position + 1], Capture); }
                catch (Exception e) { File.WriteAllText(Path.Combine(Program.Arguments[position + 1], "capture-error.txt"), e.ToString()); Environment.ExitCode = 1; }
                if (Program.Arguments.Contains("--exit-after-capture")) Close();
                return;
            }
            if (Program.Arguments.Contains("--onboarding-sizing-check"))
            {
                var position = Array.IndexOf(Program.Arguments, "--onboarding-sizing-check");
                await OnboardingVerification.CheckSizingAsync(this, Program.Arguments[position + 1], Capture);
                if (Program.Arguments.Contains("--exit-after-capture")) Close();
                return;
            }
            if (Program.Arguments.Contains("--onboarding-capture"))
            {
                var position = Array.IndexOf(Program.Arguments, "--onboarding-capture");
                try { await OnboardingVerification.RecordAsync(this, vm, OnboardingScreen, Program.Arguments[position + 1], Capture); }
                catch (Exception e) { File.WriteAllText(Path.Combine(Program.Arguments[position + 1], "capture-error.txt"), e.ToString()); Environment.ExitCode = 1; }
                if (Program.Arguments.Contains("--exit-after-capture")) Close();
                return;
            }
            if (Program.Arguments.Contains("--onboarding-restart-check"))
            {
                var position = Array.IndexOf(Program.Arguments, "--onboarding-restart-check");
                var directory = Program.Arguments[position + 1]; Directory.CreateDirectory(directory);
                await Task.Delay(500);
                File.WriteAllText(Path.Combine(directory, "restart.json"), System.Text.Json.JsonSerializer.Serialize(new { vm.HasOnboarding, Language = vm.Onboarding.SelectedLanguage.Code, ServerCount = vm.Servers.Count, NativeScale = RenderScaling }));
                Capture(Path.Combine(directory, "restart-main.png"), 1);
                if (Program.Arguments.Contains("--exit-after-capture")) Close();
                return;
            }
            if (Program.Arguments.Contains("--capture"))
            {
                try
                {
                    var index = Array.IndexOf(Program.Arguments, "--capture");
                    var directory = Path.GetFullPath(Program.Arguments[index + 1]); Directory.CreateDirectory(directory);
                    await Task.Delay(800);
                    Capture(Path.Combine(directory, "01-empty.png"));
                    await vm.LoadExamplesCommand.ExecuteAsync(null);
                    await Task.Delay(400); Capture(Path.Combine(directory, "02-manage.png"));
                    vm.AddServerCommand.Execute(null);
                    await Task.Delay(250); Capture(Path.Combine(directory, "03-add-server.png"));
                    vm.CancelEditorCommand.Execute(null);
                    Width = 1060; Height = 720;
                    await Task.Delay(350); Capture(Path.Combine(directory, "04-compact.png"));
                    Width = 1280; Height = 920;
                    vm.NavigateCommand.Execute("部署代理"); await Task.Delay(250); Capture(Path.Combine(directory, "05-deployment.png"));
                    vm.Deployment.Step = 3; vm.Deployment.GenerateKeysCommand.Execute(null);
                    await Task.Delay(250); Capture(Path.Combine(directory, "06-reality-keys.png"));
                    vm.Deployment.ResetCommand.Execute(null);
                    vm.NavigateCommand.Execute("高级功能"); await Task.Delay(250); Capture(Path.Combine(directory, "07-advanced.png"));
                    vm.NavigateCommand.Execute("设置"); await Task.Delay(250); Capture(Path.Combine(directory, "08-settings.png"));
                    vm.NavigateCommand.Execute("管理服务"); vm.OpenExporterCommand.Execute(null);
                    await Task.Delay(250); Capture(Path.Combine(directory, "09-export.png"));
                    if (vm.Exporter is { } exporter)
                    {
                        exporter.SelectedNode = exporter.Nodes.First(n => n.Protocol == NekoProxy.Core.ProxyProtocol.Hysteria2);
                        exporter.Credential = "demonstration-only"; exporter.Sni = "example.com"; exporter.GenerateQrCommand.Execute(null);
                        await Task.Delay(250); Capture(Path.Combine(directory, "10-qr.png"));
                    }
                    vm.CloseExporterCommand.Execute(null);
                    File.WriteAllText(Path.Combine(directory, "gui-capture.txt"), "Visible native Avalonia window opened. RenderTargetBitmap captured actual attached window visual tree. Empty, sample selection, editor and compact layouts rendered. Sample data explicitly loaded; remote values remain unknown.");
                    if (Program.Arguments.Contains("--exit-after-capture")) Close();
                }
                catch (Exception e) { File.WriteAllText("gui-capture-error.txt", e.ToString()); Close(); }
            }
        };
    }
    private void Capture(string path, double scale = 1)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Round(Bounds.Width * scale), (int)Math.Round(Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(this); bitmap.Save(path);
    }
}
