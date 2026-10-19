using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using NekoProxy.Core;
using System.Diagnostics;
namespace NekoProxy.Desktop;
public sealed class PlatformActions : IPlatformActions
{
    public async Task CopyAsync(string text)
    {
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard
            ?? throw new InvalidOperationException("剪贴板不可用。");
        await clipboard.SetTextAsync(text);
    }
    public Task OpenUrlAsync(string url)
    {
        if(!Infrastructure.UpdateCoordinator.TrustedPage(url))throw new ArgumentException("Untrusted release page.");
        Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return Task.CompletedTask;
    }
    public Task OpenDirectoryAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(Path.GetFullPath(directory)) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
