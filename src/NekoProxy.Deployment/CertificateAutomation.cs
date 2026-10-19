using NekoProxy.Core;
namespace NekoProxy.Deployment;

public static class CertificateAutomation
{
    private static string Q(string value)=>RemoteCommands.Quote(value);
    public static string Probe(RemoteDeploymentRequest request)
    {
        var automatic=request.Configuration.AutomaticCertificate;if(automatic is null)return "";
        var domain=request.Configuration.Client.Sni;var storage=CertificatePolicy.Storage(domain);var port=automatic.Challenge==CertificateChallenge.Http01?80:443;
        return RemoteCommands.Privileged($$"""
            # NekoProxy: ACME domain and validation port preflight, readonly
            resolved=$(getent ahosts {{Q(domain)}} | awk '{print $1}' | sort -u)
            expected=$(getent ahosts {{Q(request.Draft.Server.Host)}} | awk '{print $1}' | sort -u | tr '\n' ' ')
            expected="$expected "{{Q(request.Draft.Server.Ipv6Address??"")}}
            match=1; test -n "$resolved" || match=0
            for address in $resolved; do case " $expected " in *" $address "*) ;; *) match=0 ;; esac; done
            printf 'ACME_DNS=%s\n' "$match"
            if command -v ss >/dev/null && [ -z "$(ss -H -lnt {{Q("sport = :"+port)}})" ]; then printf 'ACME_PORT=1\n'; else printf 'ACME_PORT=0\n'; fi
            if [ -L /etc/nekoproxy/certificates ] || [ -L {{Q(storage)}} ]; then printf 'FOREIGN=1\n'; fi
            """);
    }
    public static string Prepare(RemotePaths paths,RemoteDeploymentRequest request)=>RemoteCommands.Privileged($$"""
        # NekoProxy: prepare certificate persistence after ownership checks
        set -eu
        {{Q(paths.Binary)}} version | grep -q 'with_acme'
        test ! -L /etc/nekoproxy/certificates; test ! -L {{Q(CertificatePolicy.Storage(request.Configuration.Client.Sni))}}
        install -d -m 700 /etc/nekoproxy/certificates {{Q(CertificatePolicy.Storage(request.Configuration.Client.Sni))}}
        """);
    // Runs inside the node transaction. No success before issuance and listener readiness.
    public static string Readiness(RemoteDeploymentRequest request)
    {
        var domain=request.Configuration.Client.Sni;var dir=CertificatePolicy.Storage(domain);var port=request.Configuration.Client.Port;
        var socket=request.Draft.Node.Protocol==ProxyProtocol.Hysteria2?"-lnu":"-lnt";
        return $$"""
            # NekoProxy: wait for actual ACME certificate and protocol listener
            ready=0
            for attempt in $(seq 1 90); do
              cert=$(find {{Q(dir+"/certificates")}} -type f -name '*.crt' {{(request.Configuration.AutomaticCertificate!.Staging?"-path '*staging*'":"! -path '*staging*'")}} 2>/dev/null | head -n1)
              if [ -n "$cert" ] && openssl x509 -in "$cert" -checkend 0 -noout >/dev/null 2>&1 && openssl x509 -in "$cert" -checkhost {{Q(domain)}} -noout 2>/dev/null | grep -Fq 'does match certificate'; then
                if [ -n "$(ss -H {{socket}} {{Q("sport = :"+port)}})" ]; then ready=1; break; fi
              fi
              sleep 2
            done
            test "$ready" = 1
            printf 'CERT_READY=1\n'
            """;
    }
}
