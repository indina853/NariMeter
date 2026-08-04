using System.Runtime.InteropServices;

namespace NariMeter;

internal static class NativeTheme
{
    private const int PreferredAppModeDefault   = 0x00000000;
    private const int PreferredAppModeAllowDark = 0x00000001;

    [DllImport("uxtheme.dll", EntryPoint = "#135", CharSet = CharSet.Unicode)]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", CharSet = CharSet.Unicode)]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", EntryPoint = "#132", CharSet = CharSet.Unicode)]
    private static extern bool ShouldAppsUseDarkMode();

    [DllImport("uxtheme.dll", EntryPoint = "#133", CharSet = CharSet.Unicode)]
    private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    public static bool IsSystemDarkMode()
    {
        return ShouldAppsUseDarkMode();
    }

    public static void ApplySystemTheme()
    {
        var dark = IsSystemDarkMode();
        _ = SetPreferredAppMode(dark ? PreferredAppModeAllowDark : PreferredAppModeDefault);
        FlushMenuThemes();
    }

    public static bool EnableDarkModeForWindow(IntPtr hWnd)
    {
        return AllowDarkModeForWindow(hWnd, IsSystemDarkMode());
    }
}