namespace NekoProxy.Core;

public static class ThemePreference
{
    // Keep old localized preference files readable. Unknown values use the light default.
    public static bool IsDark(string? value)=>value is "dark" or "Dark" or "深色";
    public static int Selection(string? value)=>IsDark(value)?2:value is "light" or "Light" or "浅色"?1:0;
    public static string FromSelection(int index)=>index switch{1=>"light",2=>"dark",_=>"system"};
}
