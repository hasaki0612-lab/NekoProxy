using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
namespace NekoProxy.Desktop.ViewModels;
public partial class PrivacyViewModel(LocalPrivacyService service, ISettingsStore settings, IPlatformActions platform, Func<Task> reload) : CategorizedViewModel
{
    public string DataDirectory => service.DataDirectory;
    public string Protection => PlatformSecretProtector.Description;
    [ObservableProperty] private string size = "—";
    [ObservableProperty] private string lastSaved = "尚未保存";
    [ObservableProperty] private string message = "数据留在本机。只有妳主动发起操作时才访问指定服务器。";
    [ObservableProperty] private string backupPassword = "";
    [ObservableProperty] private bool replaceOnRestore;
    [ObservableProperty] private LocalExportFile? selectedExport;
    [ObservableProperty] private bool clearServers;
    [ObservableProperty] private bool clearExports;
    [ObservableProperty] private bool clearLogs;
    [ObservableProperty] private bool clearCache;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasConfirmation;
    [ObservableProperty] private string confirmationText = "";
    private Func<Task>? _confirmed;
    public ObservableCollection<LocalExportFile> Exports { get; } = [];
    public bool HasSelectedExport=>SelectedExport is not null;
    partial void OnSelectedExportChanged(LocalExportFile? value)=>OnPropertyChanged(nameof(HasSelectedExport));
    public bool NoExports => Exports.Count == 0;
    public Func<string, Task<string?>>? PickFileAsync { get; set; }
    public Func<bool>? RemoteOperationBusy {get;set;}
    [RelayCommand] private void ResetHostTrust()
    {
        if(RemoteOperationBusy?.Invoke()==true){Message="请等部署结束后再修改本地资料。";return;}
        Confirm("清除已记住的 SSH 主机指纹？服务器资料和节点凭据会保留，下次连接必须重新核对指纹。",async()=>{await settings.UpdateAsync(s=>s with{KnownHosts=[]});Message="主机指纹缓存已清除，下次连接会请你重新确认。";});
    }
    public async Task RefreshAsync()
    {
        try { var info = service.Inspect(); Size = $"{info.Bytes / 1024.0:F1} KB"; LastSaved = info.LastSaved?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未保存"; var selected=SelectedExport?.Path;Exports.Clear(); foreach (var f in service.ListExports((await settings.LoadAsync()).ExportDirectory)) Exports.Add(f);SelectedExport=Exports.FirstOrDefault(f=>f.Path==selected); OnPropertyChanged(nameof(NoExports)); }
        catch { Message = "无法读取本地目录。"; }
    }
    [RelayCommand] private async Task OpenDataAsync() => await RunAsync(async () => { await platform.OpenDirectoryAsync(DataDirectory); Message = "已打开数据目录。"; });
    [RelayCommand] private async Task OpenExportsAsync() => await RunAsync(async () => { await platform.OpenDirectoryAsync((await settings.LoadAsync()).ExportDirectory); Message = "已打开导出目录。"; });
    [RelayCommand] private async Task OpenLogsAsync() => await RunAsync(async () => { await platform.OpenDirectoryAsync(service.LogDirectory); Message = "已打开日志目录。"; });
    [RelayCommand] private async Task BackupAsync()
    {
        if (BackupPassword.Length < 12) { Message = "备份密码至少需要 12 个字符。"; return; }
        var path = PickFileAsync is null ? null : await PickFileAsync("backup"); if (path is null) return; var password = BackupPassword;
        await RunAsync(async () => { await service.BackupAsync(path, password); Message = "加密备份已保存。请妥善保管密码。"; }); BackupPassword = "";
    }
    [RelayCommand] private async Task RequestRestoreAsync()
    {
        if(RemoteOperationBusy?.Invoke()==true){Message="请等部署结束后再修改本地资料。";return;}
        if (BackupPassword.Length == 0) { Message = "请输入备份密码。"; return; }
        var path = PickFileAsync is null ? null : await PickFileAsync("restore"); if (path is null) return; var password = BackupPassword; var replace = ReplaceOnRestore;
        Confirm(replace ? "替换本地服务器与凭据？恢复前会创建检查点。远程 VPS 不受影响。" : "合并备份中的服务器与凭据？相同 ID 使用备份记录，恢复前会创建检查点。", async () =>
        {
            try
            {
                try { await service.RestoreAsync(path, password, replace); }
                catch (CredentialStorageUnavailableException e) { Message=e.Message; return; }
                catch { Message = "恢复未完成，请检查密码、文件和现有数据；如已建立检查点，请保留它。"; return; }
                try { await reload(); Message = "备份已恢复，原始数据检查点已保留。"; }
                catch { Message = "备份已还原，但界面刷新失败。请重新启动应用。"; }
            }
            finally { BackupPassword = ""; }
        });
    }
    [RelayCommand] private void RequestDelete()
    {
        if (SelectedExport is not { } file) return;
        Confirm("删除此敏感配置文件？删除本地文件不会撤销已泄露的凭据。", async () => { service.DeleteExport((await settings.LoadAsync()).ExportDirectory, file); Message = "本地导出文件已删除。"; });
    }
    [RelayCommand] private void RequestClear()
    {
        if(RemoteOperationBusy?.Invoke()==true){Message="请等部署结束后再修改本地资料。";return;}
        var selected = new ClearSelection(ClearServers, ClearExports, ClearLogs, ClearCache);
        if (!selected.Servers && !selected.Exports && !selected.Logs && !selected.Cache) { Message = "请先选择需要清除的资料。"; return; }
        Confirm("清除选中的本地资料？此操作无法撤销，不会更改远程 VPS。", async () => { await service.ClearAsync(selected, (await settings.LoadAsync()).ExportDirectory); await reload(); Message = "选中的本地资料已清除。远程 VPS 未作更改。"; });
    }
    [RelayCommand] private async Task DiagnosticsAsync()
    {
        var path = PickFileAsync is null ? null : await PickFileAsync("diagnostics"); if (path is null) return;
        await RunAsync(async () => { await service.ExportDiagnosticsAsync(path); Message = "脱敏诊断报告已导出，不含服务器地址、路径或认证资料。"; });
    }
    private void Confirm(string text, Func<Task> action) { ConfirmationText = text; _confirmed = action; HasConfirmation = true; }
    [RelayCommand] private void CancelConfirmation() { _confirmed = null; HasConfirmation = false; BackupPassword = ""; }
    [RelayCommand] private async Task ConfirmAsync() { var action = _confirmed; _confirmed = null; HasConfirmation = false; if (action is not null) await RunAsync(action); }
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return; IsBusy = true; try { await action(); await RefreshAsync(); } catch (CredentialStorageUnavailableException e) { Message = e.Message; } catch { Message = "操作失败，请检查路径与权限。"; } finally { IsBusy = false; }
    }
}
