using System.Text.Json;
using NekoProxy.Core;
namespace NekoProxy.Infrastructure;

// No addresses, authentication material, raw commands or remote logs in history.
public sealed class MonitoringHistoryStore(string dataDirectory,TimeProvider? clock=null):IMonitoringHistoryStore
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly TimeProvider _clock=clock??TimeProvider.System;
    private string PathFor(Guid id)=>Path.Combine(dataDirectory,"history",id.ToString("N")+".json");
    public async Task<IReadOnlyList<MonitoringSample>> LoadAsync(Guid id,CancellationToken token=default)
    {
        await _gate.WaitAsync(token);
        try{return await ReadAsync(id,token);}finally{_gate.Release();}
    }
    private async Task<List<MonitoringSample>> ReadAsync(Guid id,CancellationToken token)
    {
        var path=PathFor(id);if(!File.Exists(path))return [];
        if(new FileInfo(path).Length>25000000)throw new InvalidDataException("History file exceeds the safety limit.");
        await using var stream=File.OpenRead(path);
        var rows=await JsonSerializer.DeserializeAsync<List<MonitoringSample>>(stream,cancellationToken:token)??throw new InvalidDataException("History file is invalid.");
        return rows.Where(r=>r.Timestamp>_clock.GetUtcNow().AddHours(-24)).OrderBy(r=>r.Timestamp).TakeLast(10000).ToList();
    }
    public async Task AppendAsync(Guid id,MonitoringSample sample,int retentionHours=24,CancellationToken token=default)
    {
        await _gate.WaitAsync(token);
        string? stage=null;
        try
        {
            var rows=await ReadAsync(id,token);rows.RemoveAll(r=>r.Timestamp==sample.Timestamp);
            // Keep only numerical/system data. Free-form error messages may contain addresses.
            rows.Add(sample with{Snapshot=sample.Snapshot with{Message="",Certificate=null}});
            rows=rows.Where(r=>r.Timestamp>_clock.GetUtcNow().AddHours(-Math.Clamp(retentionHours,6,24))).OrderBy(r=>r.Timestamp).TakeLast(10000).ToList();
            var path=PathFor(id);Directory.CreateDirectory(Path.GetDirectoryName(path)!);stage=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            await using(var file=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.Asynchronous))await JsonSerializer.SerializeAsync(file,rows,cancellationToken:token);
            File.Move(stage,path,true);stage=null;
            foreach(var stale in Directory.EnumerateFiles(Path.GetDirectoryName(path)!,"*.json").Where(p=>File.GetLastWriteTimeUtc(p)<_clock.GetUtcNow().AddDays(-2).UtcDateTime))File.Delete(stale);
        }
        finally{if(stage is not null&&File.Exists(stage))File.Delete(stage);_gate.Release();}
    }
}
