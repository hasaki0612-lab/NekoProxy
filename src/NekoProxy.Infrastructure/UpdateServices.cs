using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NekoProxy.Core;
namespace NekoProxy.Infrastructure;

public sealed record ReleaseInfo(string Version,Uri Page);
public interface IReleaseSource
{
    bool IsConfigured { get; }
    Task<ReleaseInfo> QueryAsync(CancellationToken cancellationToken);
}
public static class ReleaseVersions
{
    public static Version Parse(string value)
    {
        if(!Regex.IsMatch(value,@"^v?\d+\.\d+\.\d+(\.\d+)?$"))throw new InvalidDataException("Stable semantic version required.");
        var v=Version.Parse(value.TrimStart('v'));return new(v.Major,v.Minor,v.Build,Math.Max(0,v.Revision));
    }
    public static bool IsNewer(string latest,string current)=>Parse(latest)>Parse(current);
}
public sealed class GitHubReleaseSource(HttpClient client,string? repository) : IReleaseSource
{
    // This must be set to the actual publisher's repository before release.
    public static string? OfficialRepository {get;}=null;
    public bool IsConfigured=>repository is not null && Regex.IsMatch(repository,@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
    public async Task<ReleaseInfo> QueryAsync(CancellationToken cancellationToken)
    {
        if(!IsConfigured)throw new InvalidOperationException("Release channel is unconfigured.");
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("NekoProxy/"+AppVersion.Current);request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)throw new HttpRequestException("Rate limited.");
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is > 1048576)throw new InvalidDataException("Response too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bounded=new MemoryStream();var buffer=new byte[4096];int count;
        while((count=await stream.ReadAsync(buffer,cancellationToken))!=0){if(bounded.Length+count>1048576)throw new InvalidDataException("Response too large.");await bounded.WriteAsync(buffer.AsMemory(0,count),cancellationToken);}
        using var doc=JsonDocument.Parse(bounded.ToArray());var json=doc.RootElement;
        if(json.GetProperty("draft").GetBoolean()||json.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("Stable release required.");
        var tag=json.GetProperty("tag_name").GetString()!;_=ReleaseVersions.Parse(tag);
        var page=new Uri(json.GetProperty("html_url").GetString()!);
        var prefix=$"/{repository}/releases/tag/";
        if(page.Scheme!="https"||page.Host!="github.com"||!page.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal)||!string.IsNullOrEmpty(page.UserInfo)||page.Port!=443)throw new InvalidDataException("Untrusted release page.");
        return new(tag.TrimStart('v'),page);
    }
}
public enum UpdateCheckState { Unconfigured, Disabled, NotDue, Busy, Current, Available, Ignored, Failed, Cancelled }
public sealed record UpdateCheckResult(UpdateCheckState State,AppSettings Settings);
public sealed class UpdateCoordinator(ISettingsStore store,IReleaseSource source,string currentVersion,TimeProvider? timeProvider=null)
{
    private readonly TimeProvider _time=timeProvider??TimeProvider.System;
    private readonly SemaphoreSlim _gate=new(1,1);
    public bool IsConfigured=>source.IsConfigured;
    public static bool ScheduledCheckDue(AppSettings settings,DateTimeOffset now)=>settings.AutoCheckUpdates &&
        (settings.LastUpdateAttempt is null || now-settings.LastUpdateAttempt>=TimeSpan.FromHours(24));
    public async Task<UpdateCheckResult> CheckAsync(bool manual,CancellationToken cancellationToken=default)
    {
        if(!await _gate.WaitAsync(0,cancellationToken))return new(UpdateCheckState.Busy,await store.LoadAsync());
        try
        {
            var settings=await store.LoadAsync();
            if(!source.IsConfigured)return new(UpdateCheckState.Unconfigured,settings);
            if(!manual&&!settings.AutoCheckUpdates)return new(UpdateCheckState.Disabled,settings);
            var now=_time.GetUtcNow();
            if(!manual&&!ScheduledCheckDue(settings,now))return new(UpdateCheckState.NotDue,settings);
            // Persist attempts before HTTP, including failures: no repeated offline requests.
            settings=await store.UpdateAsync(s=>s with{LastUpdateAttempt=now});
            try
            {
                var release=await source.QueryAsync(cancellationToken);cancellationToken.ThrowIfCancellationRequested();
                var newer=ReleaseVersions.IsNewer(release.Version,currentVersion);
                settings=await store.UpdateAsync(s=>s with{LastUpdateSuccess=now,LatestKnownVersion=release.Version,LatestReleasePage=release.Page.AbsoluteUri});
                return new(newer?(settings.IgnoredVersion==release.Version?UpdateCheckState.Ignored:UpdateCheckState.Available):UpdateCheckState.Current,settings);
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){return new(UpdateCheckState.Cancelled,settings);}
            catch(Exception e) when(e is OperationCanceledException or HttpRequestException or IOException or JsonException or InvalidOperationException or ArgumentException)
            {return new(UpdateCheckState.Failed,settings);}
        }
        finally{_gate.Release();}
    }
    public static bool TrustedPage(string? value)
    {
        if(GitHubReleaseSource.OfficialRepository is not {} repository || !Uri.TryCreate(value,UriKind.Absolute,out var uri))return false;
        return uri.Scheme=="https"&&uri.Host=="github.com"&&uri.Port==443&&uri.UserInfo.Length==0&&uri.AbsolutePath.StartsWith($"/{repository}/releases/tag/",StringComparison.Ordinal);
    }
}
