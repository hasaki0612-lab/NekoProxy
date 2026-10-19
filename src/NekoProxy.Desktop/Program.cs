using Avalonia;
namespace NekoProxy.Desktop;
internal static class Program
{
    internal static string[] Arguments = [];
    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        if(args.Length==2&&args[0]=="--deployment-scripts"){DeploymentScriptVerification.GenerateAsync(args[1]).GetAwaiter().GetResult();return;}
        if(args.Length==2&&args[0]=="--protocol-fixtures"){ProtocolVerification.GenerateAsync(args[1]).GetAwaiter().GetResult();return;}
        if (args.Length == 2 && args[0] == "--export-fixtures")
        {
            ExportVerification.GenerateAsync(args[1]).GetAwaiter().GetResult(); return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
