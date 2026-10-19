namespace NekoProxy.Core;

public sealed record ApplicationDirectories(string Data, string Configuration, string State, string Cache)
{
    public static ApplicationDirectories Current(string? overrideDirectory = null)
    {
        if (overrideDirectory is not null)
        {
            var root = Path.GetFullPath(overrideDirectory);
            return new(root, root, root, Path.Combine(root, "cache"));
        }
        if (OperatingSystem.IsWindows())
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NekoProxy");
            return new(root, root, root, Path.Combine(root, "cache"));
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) throw new IOException("Cannot locate the current user's home directory.");
        if (OperatingSystem.IsMacOS())
        {
            var root = Path.Combine(home, "Library", "Application Support", "NekoProxy");
            return new(root, root, root, Path.Combine(home, "Library", "Caches", "NekoProxy"));
        }
        return Linux(home, Environment.GetEnvironmentVariable);
    }

    public static ApplicationDirectories Linux(string home, Func<string, string?> environment)
    {
        string Root(string variable, params string[] fallback)
        {
            var value = environment(variable);
            return Path.Combine(!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)
                ? value : Path.Combine([home, .. fallback]), "NekoProxy");
        }
        return new(Root("XDG_DATA_HOME", ".local", "share"), Root("XDG_CONFIG_HOME", ".config"),
            Root("XDG_STATE_HOME", ".local", "state"), Root("XDG_CACHE_HOME", ".cache"));
    }

    public static string DefaultExportDirectory
    {
        get
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            return Path.Combine(desktop, "NekoProxy", "订阅与配置文件");
        }
    }
}
