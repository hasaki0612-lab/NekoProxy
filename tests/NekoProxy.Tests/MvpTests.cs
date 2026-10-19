using NekoProxy.Core;
using NekoProxy.Deployment;
using NekoProxy.Infrastructure;
using NekoProxy.Exporters;
using NekoProxy.Desktop.ViewModels;
using Xunit;
using System.Text.Json;

namespace NekoProxy.Tests;
public sealed class MvpTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NekoProxy-tests", Guid.NewGuid().ToString("N"));
    private MainViewModel CreateVm() => new(new JsonServerRepository(_directory), new JsonSettingsStore(_directory), new DisconnectedServerMonitor(), new TestPlatform(), new ExportService([new SingBoxExporter(), new MihomoExporter(), new ShadowrocketExporter()]), new RealityKeyGenerator(), new PendingDeploymentService());
    [Fact] public async Task FirstRunIsEmptyAndDoesNotCreateFakeMeasurements()
    {
        var vm = CreateVm(); await vm.InitializeAsync();
        Assert.Empty(vm.Servers); Assert.Equal("管理服务", vm.Page); Assert.Null(vm.Snapshot.CpuPercent); Assert.Equal("—", vm.CpuDisplay);
    }
    [Fact] public async Task CrudPersistsAndRenamePreservesMultipleNodes()
    {
        var vm = CreateVm(); await vm.InitializeAsync(); await vm.LoadExamplesCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.SelectedServer!.Nodes.Count);
        var id = vm.SelectedServer.Id; vm.RenameServerCommand.Execute(null); vm.Editor!.Name = "renamed"; await vm.SaveServerCommand.ExecuteAsync(null);
        var reopened = CreateVm(); await reopened.InitializeAsync(); Assert.Equal("renamed", reopened.Servers.First(x => x.Id == id).Name); Assert.Equal(2, reopened.SelectedServer!.Nodes.Count);
        reopened.AddServerCommand.Execute(null); reopened.Editor!.Name = "new"; reopened.Editor.Host = "192.0.2.2"; await reopened.SaveServerCommand.ExecuteAsync(null); Assert.Equal(4, reopened.Servers.Count);
        reopened.EditServerCommand.Execute(null); reopened.Editor!.Notes = "edited"; await reopened.SaveServerCommand.ExecuteAsync(null); Assert.Equal("edited", reopened.SelectedServer!.Notes);
        reopened.RequestRemoveCommand.Execute(null); await reopened.ConfirmRemoveCommand.ExecuteAsync(null); Assert.Equal(3, reopened.Servers.Count);
        var persisted = await new JsonServerRepository(_directory).LoadAsync(); Assert.DoesNotContain(persisted, s => s.Name == "new");
    }
    [Fact] public async Task SearchUsesNameIpLocationAndProtocol()
    {
        var vm = CreateVm(); await vm.InitializeAsync(); await vm.LoadExamplesCommand.ExecuteAsync(null);
        vm.Search = "大阪"; Assert.Single(vm.FilteredServers); vm.Search = "203.0.113.10"; Assert.Single(vm.FilteredServers);
        vm.Search = "REALITY"; Assert.Single(vm.FilteredServers); vm.Search = "nonexistent"; Assert.True(vm.NoSelection);
    }
    [Fact] public async Task InvalidIpAndPortCannotBeSaved()
    {
        var vm = CreateVm(); await vm.InitializeAsync(); vm.AddServerCommand.Execute(null); vm.Editor!.Name = "invalid"; vm.Editor.Host = "not-an-ip";
        await vm.SaveServerCommand.ExecuteAsync(null); Assert.Empty(vm.Servers); Assert.NotEmpty(vm.Editor.Error);
        vm.Editor.Host = "::1"; vm.Editor.SshPort = "70000"; await vm.SaveServerCommand.ExecuteAsync(null); Assert.Empty(vm.Servers);
    }
    [Fact] public async Task CorruptStorageIsNotSilentlyOverwritten()
    {
        Directory.CreateDirectory(_directory); var path = Path.Combine(_directory, "servers.json"); await File.WriteAllTextAsync(path, "broken");
        var vm = CreateVm(); await vm.InitializeAsync(); await vm.LoadExamplesCommand.ExecuteAsync(null); Assert.Equal("broken", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task SettingsSurviveRestart()
    {
        var store = new JsonSettingsStore(_directory); await store.SaveAsync(new() { SshTimeoutSeconds = 27, ExportDirectory = _directory });
        Assert.Equal(27, (await new JsonSettingsStore(_directory).LoadAsync()).SshTimeoutSeconds);
    }
    [Fact] public async Task RefreshCannotClaimRemoteSuccess()
    {
        var vm = CreateVm(); await vm.InitializeAsync(); await vm.LoadExamplesCommand.ExecuteAsync(null); await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Null(vm.Snapshot.RefreshedAt); Assert.Equal("未连接", vm.Snapshot.SshState); Assert.Contains("尚未接入", vm.Status);
    }
    [Theory][InlineData("debian")][InlineData("ubuntu")] public void AptAdapterSharesDistroBusinessLogic(string id) => Assert.True(new AptDistributionAdapter().Supports(id));
    [Fact] public void ArchUsesPacman() { Assert.True(new PacmanDistributionAdapter().Supports("arch")); Assert.False(new AptDistributionAdapter().Supports("arch")); }
    private static ClientNodeConfiguration Node(ProxyProtocol protocol) => new("server", "node", "2001:db8::1", 443, protocol,
        protocol == ProxyProtocol.VlessReality ? "3f8dddb1-7033-4f71-86c1-2e70bfe0a503" : "a:#@ secret", "example.com", protocol == ProxyProtocol.VlessReality ? new RealityKeyGenerator().Generate().PublicKey : null, protocol == ProxyProtocol.VlessReality ? "a1b2c3d4" : null);
    [Theory][InlineData(ProxyProtocol.VlessReality)][InlineData(ProxyProtocol.Hysteria2)]
    public void SingBoxExportIsJsonAndDoesNotDisableTlsVerification(ProxyProtocol protocol)
    {
        var text = new SingBoxExporter().Generate(Node(protocol)); using var json = JsonDocument.Parse(text);
        Assert.Single(json.RootElement.GetProperty("outbounds").EnumerateArray()); Assert.DoesNotContain("insecure", text); Assert.DoesNotContain("private_key", text);
    }
    [Fact] public void LinksEncodeCredentialsAndIpv6() { var link = new ShadowrocketExporter().Generate(Node(ProxyProtocol.Hysteria2)); Assert.Contains("[2001:db8::1]", link); Assert.Contains("a%3A%23%40%20secret", link); }
    [Fact] public async Task ExportRequiresExplicitClientAndUsesUniqueSafePaths()
    {
        var service = new ExportService([new SingBoxExporter(), new MihomoExporter(), new ShadowrocketExporter()]);
        var node = Node(ProxyProtocol.Hysteria2) with { ServerName = "../../server", NodeName = "node/x" };
        var first = await service.ExportAsync(node, ClientType.SingBox, _directory); var second = await service.ExportAsync(node, ClientType.SingBox, _directory);
        Assert.NotEqual(first.FilePath, second.FilePath); Assert.StartsWith(Path.GetFullPath(_directory), first.FilePath); Assert.Equal(2, Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Length);
    }
    [Fact] public async Task DeploymentStubCannotReportSuccess() => Assert.False((await new PendingDeploymentService().DeployAsync(new(ProxyProtocol.Hysteria2, SampleProfiles.Create()[0], "preview"))).Success);
    [Fact] public void RealityGeneratorProducesMatchingX25519Keys()
    {
        var keys = new RealityKeyGenerator().Generate(); Assert.True(Guid.TryParse(keys.Uuid, out _)); Assert.Equal(16, keys.ShortId.Length);
        var bytes = Convert.FromBase64String(keys.PrivateKey.Replace('-', '+').Replace('_', '/') + "=");
        var key = new Org.BouncyCastle.Crypto.Parameters.X25519PrivateKeyParameters(bytes);
        var computed = Convert.ToBase64String(key.GeneratePublicKey().GetEncoded()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(keys.PublicKey, computed);
    }
    [Fact] public async Task NodeEditingPreservesSiblingEndpoints()
    {
        var vm = CreateVm(); await vm.InitializeAsync(); await vm.LoadExamplesCommand.ExecuteAsync(null);
        vm.AddNodeCommand.Execute(null); vm.NodeEditor!.MetadataOnly=true; vm.NodeEditor.Name = "third"; vm.NodeEditor.ProtocolIndex = 1; vm.NodeEditor.Port = "9443";
        await vm.SaveNodeCommand.ExecuteAsync(null); Assert.Equal(3, vm.SelectedServer!.Nodes.Count); Assert.Equal("third", vm.SelectedNode!.Name);
        vm.EditNodeCommand.Execute(null); vm.NodeEditor!.Port = "9444"; await vm.SaveNodeCommand.ExecuteAsync(null);
        var loaded = await new JsonServerRepository(_directory).LoadAsync(); Assert.Equal(9444, loaded[0].Nodes.Single(n => n.Name == "third").Port); Assert.Equal(3, loaded[0].Nodes.Count);
    }
    [Fact] public async Task ExportDialogCannotWriteBeforeClientSelection()
    {
        using var vm = new ExportViewModel(SampleProfiles.Create()[1], _directory, new ExportService([new SingBoxExporter()]), new TestPlatform());
        vm.Credential = "password with spaces "; vm.Sni = "example.com";
        await vm.GenerateCommand.ExecuteAsync(null); Assert.False(Directory.Exists(_directory)); Assert.Contains("选择", vm.Message);
        vm.SelectedClient = vm.Clients.Single(c => c.Type == ClientType.SingBox); await vm.GenerateCommand.ExecuteAsync(null);
        var content = await File.ReadAllTextAsync(Directory.GetFiles(_directory, "*.json", SearchOption.AllDirectories).Single());
        Assert.Contains("password with spaces ", content); vm.Dispose(); Assert.Equal("", vm.Credential);
    }
    [Fact] public async Task InvalidSettingsCannotOverwriteValidSettings()
    {
        var vm = CreateVm(); await vm.InitializeAsync(); vm.Settings.Timeout = "35"; vm.Settings.ExportDirectory = _directory; await vm.Settings.SaveCommand.ExecuteAsync(null);
        vm.Settings.Timeout = "0"; await vm.Settings.SaveCommand.ExecuteAsync(null); Assert.Equal(35, (await new JsonSettingsStore(_directory).LoadAsync()).SshTimeoutSeconds);
    }
    [Fact] public void WizardRejectsMissingKeyAndNeverClaimsDeployment()
    {
        var vm = new DeploymentViewModel(new RealityKeyGenerator(), new PendingDeploymentService()); vm.Step = 2; vm.Host = "192.0.2.1";
        vm.NextCommand.Execute(null); Assert.Equal(2, vm.Step); Assert.Contains("私钥", vm.Message);
        vm.Step = 3; vm.Host="192.0.2.1"; vm.Sni = "example.com"; vm.Uuid="";vm.NextCommand.Execute(null); Assert.Equal(3, vm.Step);
        vm.GenerateKeysCommand.Execute(null); vm.NextCommand.Execute(null); Assert.Equal(4, vm.Step); Assert.DoesNotContain("private_key", vm.Preview); Assert.Contains("待检测", vm.Preview);
        vm.ResetCommand.Execute(null); Assert.True(Guid.TryParse(vm.Uuid,out _)); Assert.Equal(2, vm.Step);
    }
    [Theory][InlineData("CON")][InlineData("NUL.txt")][InlineData("LPT1")] public void ReservedWindowsFolderNamesAreEscaped(string name) => Assert.StartsWith("_", ExportService.SafeName(name));
    [Fact] public void MalformedRealityPublicKeyIsRejected() => Assert.Throws<ArgumentException>(() => new SingBoxExporter().Generate(Node(ProxyProtocol.VlessReality) with { RealityPublicKey = "not-a-key" }));
    [Fact] public async Task UnknownHostKeyCannotOpenAnSshSession()
    {
        var profile = SampleProfiles.Create()[0] with { PrivateKeyPath = "dummy-key" };
        await Assert.ThrowsAsync<SshConnectionException>(() => new SshConnectionFactory("").ConnectAsync(profile, TimeSpan.FromSeconds(5)));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class TestPlatform : IPlatformActions
    {
        public Task CopyAsync(string text) => Task.CompletedTask;
        public Task OpenDirectoryAsync(string directory) => Task.CompletedTask;
    }
}
