using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NekoProxy.Core;
using NekoProxy.Infrastructure;
namespace NekoProxy.Desktop.ViewModels;
public partial class UpdateViewModel : ObservableObject,IDisposable
{
    public const string CurrentVersion=AppVersion.Current;
    private readonly ISettingsStore _store;
    private readonly IPlatformActions _platform;
    private readonly HttpClient _http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)};
    private readonly UpdateCoordinator _coordinator;
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _scheduledRequest;
    private bool _loading;
    public Task PendingPreferenceWrite {get;private set;}=Task.CompletedTask;
    [ObservableProperty] private bool autoCheck=true;
    [ObservableProperty] private string latestVersion="—";
    [ObservableProperty] private string lastChecked="—";
    [ObservableProperty] private string message="";
    [ObservableProperty] private bool isChecking;
    [ObservableProperty] private bool hasUpdate;
    private AppSettings _settings=new();
    public UpdateViewModel(ISettingsStore store,IPlatformActions platform)
    { _store=store;_platform=platform;_coordinator=new(store,new GitHubReleaseSource(_http,GitHubReleaseSource.OfficialRepository),CurrentVersion); }
    public void Initialize(AppSettings settings)
    {
        Apply(settings);Message=_coordinator.IsConfigured?"":Localization.LocalizationHub.Current.Catalog["zh-Hans"]["updates.unconfigured"];
        _=ScheduledLoopAsync();
    }
    public void Apply(AppSettings settings)
    {
        _settings=settings;_loading=true;AutoCheck=settings.AutoCheckUpdates;_loading=false;
        LatestVersion=settings.LatestKnownVersion??"—";LastChecked=settings.LastUpdateSuccess?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"—";
        try { HasUpdate=_coordinator.IsConfigured&&settings.LatestKnownVersion is {} v&&ReleaseVersions.IsNewer(v,CurrentVersion)&&settings.IgnoredVersion!=v; } catch(InvalidDataException){HasUpdate=false;}
        if(settings.RemindUpdateAfter is {} until&&until>DateTimeOffset.UtcNow)HasUpdate=false;
    }
    partial void OnAutoCheckChanged(bool value)
    {
        if(_loading)return;if(!value)_scheduledRequest?.Cancel();PendingPreferenceWrite=SaveAutomaticAsync(value);
    }
    private async Task SaveAutomaticAsync(bool value)
    {
        try{_settings=await _store.UpdateAsync(s=>s with{AutoCheckUpdates=value});Message="更新偏好已保存。";}
        catch{Message="设置保存失败，请检查路径与权限。";_loading=true;AutoCheck=_settings.AutoCheckUpdates;_loading=false;}
    }
    private async Task ScheduledLoopAsync()
    {
        try
        {
            await CheckAsync(false,_lifetime.Token);
            using var timer=new PeriodicTimer(TimeSpan.FromMinutes(15));
            while(await timer.WaitForNextTickAsync(_lifetime.Token))if(AutoCheck)await CheckAsync(false,_lifetime.Token);
        }
        catch(OperationCanceledException){}
    }
    [RelayCommand] private Task CheckUpdatesAsync()=>CheckAsync(true,_lifetime.Token);
    private async Task CheckAsync(bool manual,CancellationToken token)
    {
        if(IsChecking)return;
        if(!manual&&!AutoCheck)return;
        IsChecking=true;
        using var request=CancellationTokenSource.CreateLinkedTokenSource(token);
        if(!manual)_scheduledRequest=request;
        try
        {
            var result=await _coordinator.CheckAsync(manual,request.Token);if(result.State==UpdateCheckState.Cancelled)return;Apply(result.Settings);
            var key=result.State switch{UpdateCheckState.Unconfigured=>"updates.unconfigured",UpdateCheckState.Current=>"updates.current",UpdateCheckState.Available=>"updates.available",UpdateCheckState.Ignored=>"updates.ignored",UpdateCheckState.Failed=>"updates.failed",_=>null};
            if(key is not null)Message=Localization.LocalizationHub.Current.Catalog["zh-Hans"][key];
        }
        catch(OperationCanceledException){}
        catch{Message=Localization.LocalizationHub.Current.Catalog["zh-Hans"]["updates.failed"];}
        finally{if(!manual)_scheduledRequest=null;IsChecking=false;}
    }
    [RelayCommand] private async Task IgnoreAsync()
    {try{Apply(await _store.UpdateAsync(s=>s with{IgnoredVersion=LatestVersion}));HasUpdate=false;Message=Localization.LocalizationHub.Current.Catalog["zh-Hans"]["updates.ignored"];}catch{Message="设置保存失败，请检查路径与权限。";}}
    [RelayCommand] private async Task RemindLaterAsync()
    {try{Apply(await _store.UpdateAsync(s=>s with{RemindUpdateAfter=DateTimeOffset.UtcNow.AddDays(1)}));HasUpdate=false;}catch{Message="设置保存失败，请检查路径与权限。";}}
    [RelayCommand] private async Task ViewReleaseAsync()
    {
        if(!UpdateCoordinator.TrustedPage(_settings.LatestReleasePage))return;
        try{await _platform.OpenUrlAsync(_settings.LatestReleasePage!);}catch{Message=Localization.LocalizationHub.Current.Catalog["zh-Hans"]["updates.failed"];}
    }
    public void Dispose(){_lifetime.Cancel();_scheduledRequest?.Cancel();_http.Dispose();_lifetime.Dispose();}
}
