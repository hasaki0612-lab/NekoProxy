using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
using NekoProxy.Exporters;
using QRCoder;
namespace NekoProxy.Desktop.ViewModels;
public sealed record ClientChoice(string Label, ClientType Type);
public partial class ExportViewModel(ServerProfile server, string directory, IExportService service, IPlatformActions platform) : ObservableObject, IDisposable
{
    public ClientPolicyViewModel Policy {get;}=new();
    public IReadOnlyList<string> ExportScopes {get;}=["完整配置与策略","单节点链接（不含策略）"];
    [ObservableProperty] private int exportScope;
    public bool FullConfiguration=>ExportScope==0;
    public string PolicySupport=>SelectedClient?.Type==ClientType.Shadowrocket?"Shadowrocket：仅支持节点链接，无法携带此处的 DNS / IPv6 / 分流策略。请明确选择单节点链接。":"完整配置包含客户端 DNS / IPv6 / 分流策略；分享链接和二维码不含这些策略。";
    partial void OnExportScopeChanged(int value)=>OnPropertyChanged(nameof(FullConfiguration));
    public NekoProxy.Infrastructure.CredentialVault? Vault { get; init; }
    private bool _disposed;
    public Action<OperationNotice>? Feedback {get;set;}
    public string ImportGuide=>SelectedClient?.Type switch
    {
        ClientType.Shadowrocket=>"在 Shadowrocket 中从文件或二维码导入节点，选择该节点后启用连接，再访问 HTTPS 网站确认。配置含连接凭据，请勿公开分享。",
        ClientType.Mihomo=>"在 Mihomo 客户端的配置页导入生成的 YAML，启用配置并选择此节点，再验证一次 HTTPS 访问。配置检查通过不代表公网连接成功。",
        ClientType.SingBox=>"将 JSON 导入兼容的 sing-box 客户端并启用，确认核心版本兼容，再验证一次 HTTPS 访问。请妥善保管配置文件。",
        _=>"先选择正在使用的客户端，按需生成一份配置；导入并启用节点后验证 HTTPS 访问。"
    };
    [RelayCommand] private async Task RememberCredentialAsync()
    {
        try { _ = Build(); if (Vault is null || SelectedNode is null) { Message = "安全存储不可用。"; return; } await Vault.SetClientCredentialAsync(SelectedNode.Id, Credential); Message = OperatingSystem.IsWindows()?"认证资料已由当前用户的 DPAPI 加密保存。":"认证资料已通过系统密钥环保护并加密保存。"; } catch (CredentialStorageUnavailableException e) { Message=e.Message; } catch { Message = "无法安全保存认证资料。"; }
    }
    [RelayCommand] private async Task LoadCredentialAsync()
    {
        try { if (Vault is null || SelectedNode is not {} node) { Message = "安全存储不可用。"; return; } var secrets=await Vault.GetNodeSecretsAsync(node.Id);if(_disposed||SelectedNode?.Id!=node.Id)return;Credential=secrets.Credential; Message = Credential.Length > 0 ? "已读取加密认证资料。" : "此节点没有保存的认证资料。"; } catch (CredentialStorageUnavailableException e) { if(!_disposed)Message=e.Message; } catch { if(!_disposed)Message = OperatingSystem.IsWindows()?"无法解密认证资料，请使用原 Windows 用户。":"无法解密认证资料，请解锁原系统密钥环或恢复加密备份。"; }
    }
    public string ServerName => server.Name;
    public IReadOnlyList<ProxyNode> Nodes => server.Nodes;
    public IReadOnlyList<ClientChoice> Clients { get; } = [new("Shadowrocket", ClientType.Shadowrocket), new("Mihomo / Clash.Meta", ClientType.Mihomo), new("sing-box", ClientType.SingBox)];
    [ObservableProperty] private ProxyNode? selectedNode = server.Nodes.FirstOrDefault();
    [ObservableProperty] private ClientChoice? selectedClient;
    [ObservableProperty] private string credential = "";
    [ObservableProperty] private string sni = server.Nodes.FirstOrDefault()?.Sni??"";
    [ObservableProperty] private string publicKey = server.Nodes.FirstOrDefault()?.RealityPublicKey??"";
    [ObservableProperty] private string shortId = server.Nodes.FirstOrDefault()?.ShortId??"";
    [ObservableProperty] private string message = "输入已有节点的客户端参数；认证信息只保留在本次导出会话。";
    [ObservableProperty] private Bitmap? qrImage;
    public bool HasQr => QrImage is not null;
    partial void OnQrImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasQr));
    public bool IsShadowsocks => SelectedNode?.Protocol==ProxyProtocol.Shadowsocks2022;
    public string Method => SelectedNode?.Method??ShadowsocksKeys.DefaultMethod;
    public bool NeedsTls => !IsShadowsocks;
    public bool IsReality => SelectedNode?.Protocol == ProxyProtocol.VlessReality;
    public string CredentialLabel => IsReality ? "客户端 UUID" : "客户端认证密码";
    partial void OnSelectedNodeChanged(ProxyNode? value) { Credential = ""; Sni = value?.Sni??""; PublicKey = value?.RealityPublicKey??""; ShortId = value?.ShortId??""; ClearQr(); OnPropertyChanged(nameof(IsShadowsocks)); OnPropertyChanged(nameof(NeedsTls)); OnPropertyChanged(nameof(Method)); OnPropertyChanged(nameof(IsReality)); OnPropertyChanged(nameof(CredentialLabel)); }
    partial void OnCredentialChanged(string value) => ClearQr();
    partial void OnSniChanged(string value) => ClearQr();
    partial void OnPublicKeyChanged(string value) => ClearQr();
    partial void OnShortIdChanged(string value) => ClearQr();
    partial void OnSelectedClientChanged(ClientChoice? value){ClearQr();OnPropertyChanged(nameof(ImportGuide));OnPropertyChanged(nameof(PolicySupport));}
    private string CompatibleLink()
    {
        if(IsShadowsocks && SelectedClient?.Type==ClientType.Shadowrocket)throw new ArgumentException("SS2022 请使用 Mihomo 或 sing-box；Shadowrocket 版本兼容性尚未验证。");
        return ExportValidation.Link(Build());
    }
    private ClientNodeConfiguration Build()
    {
        var node = SelectedNode ?? throw new ArgumentException("请先添加并选择代理节点。");
        var config = new ClientNodeConfiguration(server.Name, node.Name, server.Host, node.Port, node.Protocol, IsReality ? Credential.Trim() : Credential, NeedsTls?Sni.Trim():"", IsReality?PublicKey.Trim():null, IsReality?ShortId.Trim():null,IsShadowsocks?Method:ShadowsocksKeys.DefaultMethod);
        ExportValidation.Check(config); return config;
    }
    [RelayCommand] private async Task GenerateAsync()
    {
        try
        {
            if (SelectedClient is null) { Message = "请主动选择目标客户端。"; Feedback?.Invoke(new(Message,NoticeLevel.Error));return; }
            if(ExportScope is not (0 or 1))throw new ArgumentException("请选择完整配置或单节点链接。");
            var node=Build();
            var result = FullConfiguration?await service.ExportAsync(node with{Policy=Policy.Build()}, SelectedClient.Type, directory):await service.ExportLinkAsync(node,SelectedClient.Type,directory);
            Message = $"已生成 {SelectedClient.Label} 配置：\n{result.FilePath}";
            Feedback?.Invoke(new("客户端配置已生成。请按窗口中的指引导入，再验证 HTTPS 访问。",NoticeLevel.Success));
        }
        catch (ArgumentException e) { Message = e.Message; Feedback?.Invoke(new(Message,NoticeLevel.Error)); }
        catch { Message = "导出失败。请检查节点参数与目录权限。"; Feedback?.Invoke(new(Message,NoticeLevel.Error)); }
    }
    [RelayCommand] private async Task CopyLinkAsync()
    { try { await platform.CopyAsync(CompatibleLink()); Message = "兼容节点链接已复制，仅含节点参数，不含 DNS、IPv6 或分流策略。"; } catch (ArgumentException e) { Message = e.Message; } catch { Message = "复制失败。"; } }
    [RelayCommand] private void GenerateQr()
    {
        try { var bytes = PngByteQRCodeHelper.GetQRCode(CompatibleLink(), QRCodeGenerator.ECCLevel.Q, 5); ClearQr(); using var stream = new MemoryStream(bytes); QrImage = new Bitmap(stream); Message = "二维码已生成，仅包含客户端节点链接。"; }
        catch (ArgumentException e) { Message = e.Message; } catch { Message = "二维码生成失败，请检查节点参数。"; }
    }
    private void ClearQr() { QrImage?.Dispose(); QrImage = null; }
    public void Dispose() { _disposed=true;ClearQr(); Credential = ""; }
}
