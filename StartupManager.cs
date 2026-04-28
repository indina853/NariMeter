using System;
using Microsoft.Win32;

namespace NariMeter;

public static class StartupManager
{
    private const string RegistryKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName      = "NariMeter";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
            return key?.GetValue(AppName) is string path
                && path.Equals(AppPath(), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            key?.SetValue(AppName, AppPath());
        }
        catch { }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
    }

    private static string AppPath() =>
        $"\"{Environment.ProcessPath}\"";
}
