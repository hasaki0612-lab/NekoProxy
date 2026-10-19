using System.Runtime.InteropServices;
namespace NekoProxy.Desktop.Onboarding;
public interface IMotionPreferences { bool SystemReducesMotion { get; } }
public sealed class MotionPreferences : IMotionPreferences
{
    public bool SystemReducesMotion => OperatingSystem.IsWindows() &&
        SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION: read only */, 0, out var enabled, 0) && !enabled;
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool result, uint flags);
}
