using System.Globalization;
using System.Text.RegularExpressions;
using NekoProxy.Core;
namespace NekoProxy.Infrastructure;

public sealed class SshServerMonitor(ISshConnectionFactory connections):IServerMonitor
{
    public Task<ServiceSnapshot> RefreshAsync(ServerProfile server,AppSettings settings,CancellationToken token=default)=>RefreshNodeAsync(server,server.Nodes.FirstOrDefault(),settings,token);
    public async Task<ServiceSnapshot> RefreshNodeAsync(ServerProfile server,ProxyNode? node,AppSettings settings,CancellationToken token=default)
    {
        try
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            await using var session=await connections.ConnectAsync(server,TimeSpan.FromSeconds(settings.SshTimeoutSeconds),token);watch.Stop();
            var unit=node?.ServiceUnit??"sing-box.service";
            if(!Regex.IsMatch(unit,@"^(sing-box|nekoproxy-[a-f0-9]{32})\.service$"))throw new InvalidDataException();
            var state=(await session.ExecuteAsync("systemctl is-active '"+unit+"' 2>/dev/null || true",token)).Trim();
            var first=await session.ExecuteAsync(Probe,token);await Task.Delay(700,token);var second=await session.ExecuteAsync(Probe,token);
            var a=Parse(first);var b=Parse(second);double? cpu=null;long? upload=null,download=null;
            if(b.Total>a.Total&&b.Total>0)cpu=Math.Clamp(100d*(1-(b.Idle-a.Idle)/(double)(b.Total-a.Total)),0,100);
            if(b.Uptime>a.Uptime&&a.NetValid&&b.NetValid){upload=(long)Math.Max(0,(b.Tx-a.Tx)/(b.Uptime-a.Uptime));download=(long)Math.Max(0,(b.Rx-a.Rx)/(b.Uptime-a.Uptime));}
            TimeSpan? runtime=null;
            if(state=="active")
            {
                var start=await session.ExecuteAsync("systemctl show '"+unit+"' -p ActiveEnterTimestampMonotonic --value",token);
                if(double.TryParse(start.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var micros)&&micros>0&&b.Uptime>0)runtime=TimeSpan.FromSeconds(Math.Max(0,b.Uptime-micros/1000000));
            }
            var system=await session.ExecuteAsync(". /etc/os-release; printf 'DISTRO=%s\\nARCH=%s\\n' \"$PRETTY_NAME\" \"$(uname -m)\"; df -Pk / | tail -n1 | awk '{print \"DISK_TOTAL=\" $2 \"\\nDISK_USED=\" $3 \"\\nDISK_PERCENT=\" $5}'; if command -v sing-box >/dev/null; then sing-box version | head -n1; elif [ -x /opt/nekoproxy/sing-box/1.14.3/sing-box ]; then /opt/nekoproxy/sing-box/1.14.3/sing-box version | head -n1; fi",token);
            string Field(string name)=>system.Split('\n').FirstOrDefault(x=>x.StartsWith(name+"=",StringComparison.Ordinal))?[(name.Length+1)..].Trim()??"";
            long? Bytes(string name)=>long.TryParse(Field(name),out var n)&&n>=0?n*1024:null;
            double? disk=double.TryParse(Field("DISK_PERCENT").TrimEnd('%'),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?Math.Clamp(d,0,100):null;
            CertificateHealth? certificate=null;
            if(node?.Protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.TrojanTls)try{certificate=await CertificateInspector.ReadAsync(session,node,token);}catch(OperationCanceledException){throw;}catch{certificate=new(node.Sni,"无法读取",null,null,"尚未读取续期记录",DateTimeOffset.UtcNow);}
            return new("已连接",state switch{"active"=>"运行中","inactive"=>"已停止","failed"=>"启动失败",_=>"未找到该服务"},cpu,b.Memory,upload,download,runtime,DateTimeOffset.UtcNow,"状态已刷新。上传和下载速率来自这次采样。")
            {Success=true,Distribution=Field("DISTRO"),Architecture=Field("ARCH"),KernelVersion=system.Split('\n').FirstOrDefault(x=>x.StartsWith("sing-box version "))?[17..],SystemUptime=b.Uptime>0?TimeSpan.FromSeconds(b.Uptime):null,
             MemoryTotalBytes=b.MemoryTotal,MemoryPercent=b.Memory is {} used&&b.MemoryTotal is {} totalMemory&&totalMemory>0?100d*used/totalMemory:null,DiskPercent=disk,DiskTotalBytes=Bytes("DISK_TOTAL"),DiskUsedBytes=Bytes("DISK_USED"),SshConnectMilliseconds=session.ConnectionElapsed?.TotalMilliseconds,Certificate=certificate};
        }
        catch(OperationCanceledException){throw;}
        catch(SshConnectionException e){return ServiceSnapshot.Disconnected with{SshState="连接失败",Message=e.Message,ConnectionFailure=e.Kind};}
        catch{return ServiceSnapshot.Disconnected with{SshState="连接失败",Message="暂时无法读取服务状态，请检查连接和服务器权限。"};}
    }
    public const string Probe="printf 'UPTIME='; cut -d' ' -f1 /proc/uptime; head -n1 /proc/stat; cat /proc/meminfo; printf 'NET\\n'; cat /proc/net/dev";
    public sealed record Counters(double Uptime,long Total,long Idle,long? Memory,long Rx,long Tx,bool NetValid){public long? MemoryTotal {get;init;}}
    public static Counters Parse(string text)
    {
        var lines=text.Split('\n');double uptime=0;long total=0,idle=0,memTotal=0,available=0,rx=0,tx=0;bool net=false,memoryAvailable=false;
        foreach(var line in lines)
        {
            var t=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if(line.StartsWith("UPTIME="))double.TryParse(line[7..],NumberStyles.Float,CultureInfo.InvariantCulture,out uptime);
            if(t.Length>4&&t[0]=="cpu")
            {
                var values=t.Skip(1).Take(8).Select(v=>long.TryParse(v,out var n)?n:0).ToArray();total=values.Sum();idle=values[3]+(values.Length>4?values[4]:0);
            }
            if(t.Length>1&&t[0]=="MemTotal:")long.TryParse(t[1],out memTotal);
            if(t.Length>1&&t[0]=="MemAvailable:")memoryAvailable=long.TryParse(t[1],out available)&&available>=0;
            if(line.Contains(':')&&t.Length>=17&&t[0].EndsWith(':')&&t[0] is not "lo:"&& !t[0].StartsWith("veth")&&!t[0].StartsWith("docker")&&!t[0].StartsWith("br-"))
            {if(long.TryParse(t[1],out var r)&&long.TryParse(t[9],out var w)){rx+=r;tx+=w;net=true;}}
        }
        return new(uptime,total,idle,memTotal>0&&memoryAvailable?Math.Max(0,memTotal-available)*1024:null,rx,tx,net){MemoryTotal=memTotal>0?memTotal*1024:null};
    }
}
