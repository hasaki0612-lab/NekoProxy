using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using NekoProxy.Core;
using NekoProxy.Exporters;
using Xunit;
namespace NekoProxy.Tests;

[CollectionDefinition("Client cores",DisableParallelization=true)]public sealed class ClientCoreCollection;
[Collection("Client cores")]
public class ClientCoreBehaviorTests
{
    [ClientCoreFact]public async Task BothActualCoresEnforceLanAndIpv6PolicyAndReturnFakeIp()
    {
        var root=Environment.GetEnvironmentVariable("NEKOPROXY_CORE_ROOT")!;
        foreach(var client in new[]{ClientType.SingBox,ClientType.Mihomo})foreach(var mode in new[]{PrivacyMode.Smart,PrivacyMode.Privacy,PrivacyMode.Custom})
        {
            var dir=Path.Combine(Path.GetTempPath(),"NekoProxy-behavior-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            using var destination=new TcpListener(IPAddress.Loopback,0);destination.Start();var target=((IPEndPoint)destination.LocalEndpoint).Port;
            var accepted=0;using var stop=new CancellationTokenSource();
            var sink=Task.Run(async()=>{try{while(!stop.IsCancellationRequested){using var c=await destination.AcceptTcpClientAsync(stop.Token);Interlocked.Increment(ref accepted);await c.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"u8.ToArray(),stop.Token);}}catch(OperationCanceledException){}});
            var proxy=FreePort();var dns=FreePort();var policy=new ClientPrivacyPolicy{Mode=mode,FakeIp=mode==PrivacyMode.Custom,RejectDomains=mode==PrivacyMode.Custom?["blocked.example"]:[]};
            var n=ClientPrivacyTests.Node() with{Policy=policy};string config;string exe;string[] args;
            if(client==ClientType.SingBox)
            {
                var data=JsonNode.Parse(new SingBoxExporter().Generate(n))!;data["inbounds"]![0]!["listen_port"]=proxy;data["inbounds"]![1]!["listen_port"]=dns;
                config=Path.Combine(dir,"client.json");await File.WriteAllTextAsync(config,data.ToJsonString());exe=TestCorePaths.SingBox(root);args=["run","-c",config];
            }
            else{config=Path.Combine(dir,"client.yaml");await File.WriteAllTextAsync(config,new MihomoExporter().Generate(n).Replace("mixed-port: 7890","mixed-port: "+proxy).Replace("127.0.0.1:1053","127.0.0.1:"+dns));exe=TestCorePaths.Mihomo(root);args=["-d",dir,"-f",config];}
            var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in args)start.ArgumentList.Add(a);
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            try
            {
                await WaitListening(proxy,process);
                var result=await Connect(proxy,"127.0.0.1:"+target);
                Assert.True((mode!=PrivacyMode.Privacy)==result.Contains("\r\n\r\nOK"),client+" / "+mode+" returned: "+result);
                if(mode==PrivacyMode.Privacy)
                {
                    Assert.Equal(0,Volatile.Read(ref accepted));
                    Assert.DoesNotContain("\r\n\r\nOK",await Connect(proxy,"[::1]:"+target));
                    var answer=await DnsQuery(dns,28);Assert.Equal(0,(answer[6]<<8)|answer[7]);
                }
                if(mode==PrivacyMode.Custom)
                {
                    var answer=await DnsQuery(dns,1);Assert.True(answer.Length>=16);Assert.True(answer[^4]==198&&answer[^3] is 18 or 19,"Expected actual core Fake-IP mapping");
                    Assert.DoesNotContain("\r\n\r\nOK",await Connect(proxy,"blocked.example:443"));
                }
            }
            finally
            {
                if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await output;await error;stop.Cancel();destination.Stop();await sink;Directory.Delete(dir,true);
            }
        }
    }
    private static int FreePort(){using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();return ((IPEndPoint)listener.LocalEndpoint).Port;}
    private static async Task WaitListening(int port,Process process)
    {for(var i=0;i<70;i++){Assert.False(process.HasExited,"Core exited before its listener was ready");try{using var c=new TcpClient();await c.ConnectAsync(IPAddress.Loopback,port);return;}catch(SocketException){await Task.Delay(100);}}throw new TimeoutException("Core listener not ready");}
    private static async Task<string> Connect(int proxy,string destination)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));using var c=new TcpClient();await c.ConnectAsync(IPAddress.Loopback,proxy,timeout.Token);var s=c.GetStream();await s.WriteAsync(Encoding.ASCII.GetBytes("CONNECT "+destination+" HTTP/1.1\r\nHost: "+destination+"\r\n\r\n"),timeout.Token);var b=new byte[2048];
        var result=new StringBuilder();
        try{while(true){var count=await s.ReadAsync(b,timeout.Token);if(count==0)break;result.Append(Encoding.ASCII.GetString(b,0,count));if(result.ToString().Contains("\r\n\r\nOK"))break;}return result.ToString();}catch(IOException){return result.ToString();}catch(OperationCanceledException){return result.ToString();}
    }
    private static async Task<byte[]> DnsQuery(int port,int type)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));using var c=new UdpClient();
        var bytes=new List<byte>{0x60,0x60,1,0,0,1,0,0,0,0,0,0};foreach(var label in "example.org".Split('.')){bytes.Add((byte)label.Length);bytes.AddRange(Encoding.ASCII.GetBytes(label));}bytes.AddRange([0,0,(byte)type,0,1]);
        await c.SendAsync(bytes.ToArray(),new IPEndPoint(IPAddress.Loopback,port),timeout.Token);return (await c.ReceiveAsync(timeout.Token)).Buffer;
    }
}
