using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NariMeter;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        NativeTheme.ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                NativeTheme.ApplySystemTheme();
        };
        Application.Run(new TrayApp());
    }
}
