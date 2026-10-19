using CommunityToolkit.Mvvm.ComponentModel;
using NekoProxy.Core;
namespace NekoProxy.Desktop.ViewModels;

public partial class ServerEditorViewModel : ObservableObject
{
    public ServerProfile? Original { get; }
    public bool RenameOnly { get; }
    public bool FullEdit => !RenameOnly;
    public string Title => RenameOnly ? "重命名服务器" : Original is null ? "添加服务器" : "编辑服务器";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string host = "";
    [ObservableProperty] private string ipv6Address="";
    [ObservableProperty] private string location = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string sshPort = "22";
    [ObservableProperty] private string username = "root";
    [ObservableProperty] private string privateKeyPath = "";
    public IReadOnlyList<string> AuthenticationChoices {get;}=["SSH 私钥","SSH 登录密码"];
    [ObservableProperty] private int authenticationIndex;
    [ObservableProperty] private string loginSecret="";
    public string LoginSecretLabel=>IsPassword?"SSH 登录密码":"SSH 私钥口令（可选）";
    public bool IsPassword=>AuthenticationIndex==1;
    partial void OnAuthenticationIndexChanged(int value){ClearSecret();OnPropertyChanged(nameof(IsPassword));OnPropertyChanged(nameof(LoginSecretLabel));}
    public void ClearSecret()=>LoginSecret="";
    [ObservableProperty] private string error = "";
    public ServerEditorViewModel(ServerProfile? original = null, bool renameOnly = false)
    {
        Original = original; RenameOnly = renameOnly;
        if (original is null) return;
        Ipv6Address=original.Ipv6Address;AuthenticationIndex=(int)original.Authentication;Name = original.Name; Host = original.Host; Location = original.Location; Notes = original.Notes;
        SshPort = original.SshPort.ToString(); Username = original.Username; PrivateKeyPath = original.PrivateKeyPath ?? "";
    }
    public ServerProfile? Build()
    {
        Error = "";
        if (!int.TryParse(SshPort, out var port)) { Error = "SSH 端口必须是整数。"; return null; }
        var profile = new ServerProfile(Original?.Id ?? Guid.NewGuid(), Name.Trim(), Host.Trim(),
            string.IsNullOrWhiteSpace(Location) ? "位置未设置" : Location.Trim(), Notes.Trim(), port, Username.Trim(),
            IsPassword||string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath.Trim(), Original?.Nodes.ToList() ?? []){Authentication=(SshAuthenticationKind)AuthenticationIndex,Ipv6Address=Ipv6Address.Trim()};
        Error = ProfileValidation.Validate(profile) ?? "";
        return Error.Length == 0 ? profile : null;
    }
}
