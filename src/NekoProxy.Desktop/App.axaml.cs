using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
using NekoProxy.Deployment;
using NekoProxy.Exporters;
using NekoProxy.Desktop.ViewModels;

namespace NekoProxy.Desktop;
public partial class App : Application
{
    private ServiceProvider? _services;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataIndex = Array.IndexOf(Program.Arguments, "--data-dir");
            var directories = ApplicationDirectories.Current(dataIndex >= 0 && Program.Arguments.Length > dataIndex + 1 ? Program.Arguments[dataIndex + 1] : null);
            var dataDirectory = directories.Data;
            var services = new ServiceCollection();
            services.AddSingleton<IServerRepository>(new JsonServerRepository(dataDirectory));
            services.AddSingleton<ISettingsStore>(new JsonSettingsStore(directories.Configuration));
            services.AddSingleton<HostTrustViewModel>();
            services.AddSingleton<SshSecretViewModel>();
            services.AddSingleton<ISshSecretSource>(p=>p.GetRequiredService<SshSecretViewModel>());
            services.AddSingleton<IHostKeyApproval>(p=>p.GetRequiredService<HostTrustViewModel>());
            services.AddSingleton<SshConnectionFactory>(p=>new SshConnectionFactory(p.GetRequiredService<ISettingsStore>(),p.GetRequiredService<IHostKeyApproval>(),p.GetRequiredService<ISshSecretSource>()));
            services.AddSingleton<SshSessionPool>();
            services.AddSingleton<ISshConnectionFactory>(p=>p.GetRequiredService<SshSessionPool>());
            services.AddSingleton<ISshSessionControl>(p=>p.GetRequiredService<SshSessionPool>());
            services.AddSingleton<ConnectionJournal>(p=>p.GetRequiredService<SshConnectionFactory>().Journal);
            services.AddSingleton<INetworkDiagnostics,NetworkDiagnostics>();
            services.AddSingleton<IRealityTargetService,RealityTargetService>();
            services.AddSingleton<IInteractiveSshFactory>(p=>p.GetRequiredService<SshConnectionFactory>());
            services.AddSingleton<IMonitoringHistoryStore>(new MonitoringHistoryStore(directories.State));
            services.AddSingleton<IRemoteAdministration,RemoteAdministration>();
            services.AddSingleton<IServerSecurityInspector,ServerSecurityInspector>();
            services.AddSingleton<IServerMonitor, SshServerMonitor>();
            services.AddSingleton<IPlatformActions, PlatformActions>();
            services.AddSingleton<IClientExporter, SingBoxExporter>();
            services.AddSingleton<IClientExporter, MihomoExporter>();
            services.AddSingleton<IClientExporter, ShadowrocketExporter>();
            services.AddSingleton<IExportService, ExportService>();
            services.AddSingleton<IRealityKeyGenerator, RealityKeyGenerator>();
            services.AddSingleton<IServerConfigurationGenerator, ServerConfigurationGenerator>();
            services.AddSingleton<IKernelReleaseCatalog,PinnedKernelReleaseCatalog>();
            services.AddSingleton<IRemoteDeploymentService,RemoteDeploymentService>();
            services.AddSingleton<IDeploymentService>(p=>p.GetRequiredService<IRemoteDeploymentService>());
            services.AddSingleton<ISecretProtector>(_ => PlatformSecretProtector.Create(dataDirectory));
            services.AddSingleton(p => new CredentialVault(dataDirectory, p.GetRequiredService<ISecretProtector>()));
            services.AddSingleton(p => new LocalPrivacyService(dataDirectory, p.GetRequiredService<ISettingsStore>(), p.GetRequiredService<IServerRepository>(), p.GetRequiredService<CredentialVault>(), directories.Configuration, directories.State, directories.Cache));
            services.AddSingleton<MainViewModel>();
            _services = services.BuildServiceProvider();
            desktop.MainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>(),
                Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://NekoProxy.Desktop/Assets/NekoProxy.ico"))) };
            ((MainWindow)desktop.MainWindow).ConfigureWindow(_services.GetRequiredService<ISettingsStore>());
            desktop.Exit += (_, _) => _services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
