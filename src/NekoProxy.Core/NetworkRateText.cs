namespace NekoProxy.Core;
public static class NetworkRateText
{
    public static string FormatMiB(double? value)=>value is not {} v?"—":v==0?"0 B/s":v<1d/1024?$"{v*1048576:0.#} B/s":v<1?$"{v*1024:0.#} KiB/s":$"{v:0.##} MiB/s";
}
