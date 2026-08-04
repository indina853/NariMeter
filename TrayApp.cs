using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NariMeter;

public sealed class TrayApp : ApplicationContext
{
    private const int ActiveIntervalMs       = 2000;
    private const int TransitionIntervalMs   = 500;
    private const int PoweredOffIntervalMs   = 1000;
    private const int DisconnectedIntervalMs = 5000;
    private const int FirstReadingIntervalMs = 1000;
    private const int ActiveThreshold        = 4;

    private const uint MF_String    = 0x0000;
    private const uint MF_Check     = 0x0008;
    private const uint MF_Popup     = 0x0010;
    private const uint MF_Separator = 0x0800;

    private const uint TpmReturnCmd   = 0x0100;
    private const uint TpmNonotify    = 0x0080;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmTopAlign    = 0x0000;
    private const uint TpmBottomAlign = 0x0020;
    private const uint WM_Null        = 0x0000;

    private const int CmdStartup       = 1001;
    private const int CmdNotify        = 1002;
    private const int CmdExit          = 1099;
    private const int WarnCmdBase      = 2000;
    private const int CritCmdBase      = 3000;

    private readonly NotifyIcon _tray;
    private readonly BatteryReader _reader;
    private readonly DeviceNotifier _notifier;
    private readonly System.Windows.Forms.Timer _timer;
    private Icon? _iconHeadphone;
    private Icon? _iconGreen;
    private Icon? _iconYellow;
    private Icon? _iconRed;
    private Icon? _iconCharging;
    private readonly Form _menuAnchor;

    private HeadsetState _lastState = HeadsetState.Disconnected;
    private bool         _initialized = false;
    private int          _activeConfirm = 0;
    private bool         _notifiedWarn = false;
    private bool         _notifiedCrit = false;
    private bool         _notifiedCharged = false;
    private bool         _notificationsEnabled;
    private int          _cachedPercent = 0;
    private ChargeStatus _cachedStatus  = ChargeStatus.Discharging;
    private int          _lowBatteryWarn;
    private int          _lowBatteryCrit;

    public TrayApp()
    {
        _notificationsEnabled = StateStore.LoadNotificationsEnabled();
        _lowBatteryWarn       = StateStore.LoadLowBatteryWarn();
        _lowBatteryCrit       = StateStore.LoadLowBatteryCrit();

        _reader = new BatteryReader();

        _cachedPercent = _reader.NeedsFirstReading ? 0 : StateStore.LoadLastPercent();

        _menuAnchor = new Form
        {
            ShowInTaskbar   = false,
            FormBorderStyle = FormBorderStyle.None,
            Location        = new Point(0, 0),
            Size            = new Size(1, 1)
        };
        _ = _menuAnchor.Handle;
        NativeTheme.EnableDarkModeForWindow(_menuAnchor.Handle);

        _tray = new NotifyIcon
        {
            Visible = true,
            Icon    = _iconHeadphone,
            Text    = "Disconnected"
        };
        _tray.MouseUp += OnTrayMouseUp;

        _notifier = new DeviceNotifier();
        _notifier.DeviceArrived += OnDeviceArrived;
        _notifier.DeviceRemoved += OnDeviceRemoved;

        _timer = new System.Windows.Forms.Timer { Interval = TransitionIntervalMs };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnDeviceArrived(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Interval = TransitionIntervalMs;
        _timer.Start();
        OnTick(this, EventArgs.Empty);
    }

    private void OnDeviceRemoved(object? sender, EventArgs e)
    {
        UsbDevice.CloseDevice();
        UsbDevice.Reset();
        _activeConfirm = 0;

        if (_lastState.Status != ChargeStatus.Disconnected)
        {
            _lastState = HeadsetState.Disconnected;
            ResetNotificationFlags();
            UpdateTray(_lastState);

            if (_notificationsEnabled)
                ShowNotification("Headset Disconnected", "Razer Nari", ToolTipIcon.Warning);
        }

        _timer.Stop();
        _timer.Interval = DisconnectedIntervalMs;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var state = _reader.Poll();

        if (!_initialized)
        {
            _initialized = true;
            if (state.IsInactive)
            {
                _lastState = state;
                UpdateTray(state);
            }
            ScheduleNext(state);
            return;
        }

        if (state.IsInactive)
            HandleInactive(state);
        else
            HandleActive(state);

        ScheduleNext(_lastState);
    }

    private void HandleInactive(HeadsetState state)
    {
        _activeConfirm = 0;

        if (!_lastState.IsInactive || _lastState.Status != state.Status)
        {
            _lastState = state;
            ResetNotificationFlags();
            UpdateTray(state);

            if (_notificationsEnabled)
            {
                if (state.Status == ChargeStatus.Disconnected)
                    ShowNotification("Headset Disconnected", "Razer Nari", ToolTipIcon.Warning);
                else if (state.Status == ChargeStatus.PoweredOff)
                    ShowNotification("Headset Powered Off", "Razer Nari", ToolTipIcon.Info);
            }
        }
    }

    private void HandleActive(HeadsetState state)
    {
        if (_lastState.IsInactive)
        {
            _activeConfirm++;
            if (_activeConfirm < ActiveThreshold) return;

            _activeConfirm = 0;
            _lastState     = state.BatteryPercent > 0
                ? state
                : HeadsetState.FromCache(_cachedPercent, _cachedStatus);
            UpdateTray(_lastState);

            if (_notificationsEnabled)
                ShowNotification("Headset Powered On", "Razer Nari", ToolTipIcon.Info);
            return;
        }

        if (state.BatteryPercent == 0)
        {
            UpdateTray(HeadsetState.FromCache(_cachedPercent, _cachedStatus));
            return;
        }

        _cachedPercent = state.BatteryPercent;
        _cachedStatus  = state.Status;

        var previous = _lastState;
        _lastState = state;
        UpdateTray(state);
        CheckNotifications(previous, state);
    }

    private void ScheduleNext(HeadsetState state)
    {
        int interval;

        if (UsbDevice.TransitionPending || _activeConfirm > 0)
            interval = TransitionIntervalMs;
        else if (state.Status == ChargeStatus.Disconnected)
            interval = DisconnectedIntervalMs;
        else if (state.Status == ChargeStatus.PoweredOff)
            interval = PoweredOffIntervalMs;
        else if (_reader.NeedsFirstReading)
            interval = FirstReadingIntervalMs;
        else
            interval = ActiveIntervalMs;

        if (_timer.Interval != interval)
            _timer.Interval = interval;
    }

    private void CheckNotifications(HeadsetState previous, HeadsetState current)
    {
        if (previous.Status == ChargeStatus.Charging &&
            current.Status  == ChargeStatus.Discharging)
        {
            _reader.NotifyCableRemoved();
        }

        if (!_notificationsEnabled) return;

        if (current.Status == ChargeStatus.FullyCharged &&
            previous.Status == ChargeStatus.Charging &&
            !_notifiedCharged)
        {
            _notifiedCharged = true;
            ShowNotification("Fully Charged", "Your headset is at 100%.", ToolTipIcon.Info);
            return;
        }

        if (current.Status != ChargeStatus.Discharging) return;

        if (current.BatteryPercent <= _lowBatteryCrit && !_notifiedCrit)
        {
            _notifiedCrit = true;
            ShowNotification("Battery Critical", $"Battery at {current.BatteryPercent}%. Plug in soon.", ToolTipIcon.Error);
            return;
        }

        if (current.BatteryPercent <= _lowBatteryWarn && !_notifiedWarn)
        {
            _notifiedWarn = true;
            ShowNotification("Battery Low", $"Battery at {current.BatteryPercent}%.", ToolTipIcon.Warning);
        }

        if (current.BatteryPercent > _lowBatteryWarn)
        {
            _notifiedWarn = false;
            _notifiedCrit = false;
        }
    }

    private void ShowNotification(string title, string text, ToolTipIcon icon)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText  = text;
        _tray.BalloonTipIcon  = icon;
        _tray.ShowBalloonTip(5000);
    }

    private void ResetNotificationFlags()
    {
        _notifiedWarn    = false;
        _notifiedCrit    = false;
        _notifiedCharged = false;
    }

    private void UpdateTray(HeadsetState state)
    {
        _tray.Icon = ResolveIcon(state);
        _tray.Text = state.TooltipLine;
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        var menu = BuildMenu();
        try
        {
            NativeTheme.ApplySystemTheme();
            NativeTheme.EnableDarkModeForWindow(_menuAnchor.Handle);
            SetForegroundWindow(_menuAnchor.Handle);

            var work  = Screen.FromPoint(Cursor.Position).WorkingArea;
            var flags = TpmReturnCmd | TpmNonotify | TpmRightButton |
                        (Cursor.Position.Y + work.Height / 2 > work.Bottom
                            ? TpmBottomAlign
                            : TpmTopAlign);

            var cmd = TrackPopupMenu(
                menu,
                flags,
                Cursor.Position.X,
                Cursor.Position.Y,
                0,
                _menuAnchor.Handle,
                IntPtr.Zero);

            PostMessage(_menuAnchor.Handle, WM_Null, UIntPtr.Zero, UIntPtr.Zero);

            if (cmd != 0)
                ExecuteMenuCommand(cmd);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private IntPtr BuildMenu()
    {
        var menu     = CreatePopupMenu();
        var warnMenu = CreatePopupMenu();
        var critMenu = CreatePopupMenu();

        AppendMenu(menu, StartupManager.IsEnabled() ? MF_String | MF_Check : MF_String, (UIntPtr)CmdStartup, "Run at Startup");
        AppendMenu(menu, MF_Separator, UIntPtr.Zero, null);
        AppendMenu(menu, _notificationsEnabled ? MF_String | MF_Check : MF_String, (UIntPtr)CmdNotify, "Show Notifications");
        AppendMenu(menu, MF_String | MF_Popup, (UIntPtr)warnMenu, "Warn Threshold");
        AppendMenu(menu, MF_String | MF_Popup, (UIntPtr)critMenu, "Crit Threshold");
        AppendMenu(menu, MF_Separator, UIntPtr.Zero, null);
        AppendMenu(menu, MF_String, (UIntPtr)CmdExit, "Exit");

        foreach (var pct in new[] { 10, 15, 20, 25, 30 })
            AppendMenu(warnMenu, pct == _lowBatteryWarn ? MF_String | MF_Check : MF_String, (UIntPtr)(WarnCmdBase + pct), $"{pct}%");

        foreach (var pct in new[] { 5, 10, 15 })
            AppendMenu(critMenu, pct == _lowBatteryCrit ? MF_String | MF_Check : MF_String, (UIntPtr)(CritCmdBase + pct), $"{pct}%");

        return menu;
    }

    private void ExecuteMenuCommand(int cmd)
    {
        switch (cmd)
        {
            case CmdStartup:
                if (StartupManager.IsEnabled()) StartupManager.Disable();
                else StartupManager.Enable();
                break;

            case CmdNotify:
                _notificationsEnabled = !_notificationsEnabled;
                StateStore.SaveNotificationsEnabled(_notificationsEnabled);
                break;

            case CmdExit:
                _tray.Visible = false;
                Application.Exit();
                break;

            default:
                if (cmd >= CritCmdBase)
                {
                    var p = cmd - CritCmdBase;
                    if (p >= _lowBatteryWarn)
                    {
                        _lowBatteryWarn = p + 5 > 30 ? 30 : p + 5;
                        StateStore.SaveLowBatteryWarn(_lowBatteryWarn);
                    }
                    _lowBatteryCrit = p;
                    StateStore.SaveLowBatteryCrit(p);
                }
                else if (cmd >= WarnCmdBase)
                {
                    var p = cmd - WarnCmdBase;
                    if (p <= _lowBatteryCrit)
                    {
                        _lowBatteryCrit = p - 5 < 5 ? 5 : p - 5;
                        StateStore.SaveLowBatteryCrit(_lowBatteryCrit);
                    }
                    _lowBatteryWarn = p;
                    StateStore.SaveLowBatteryWarn(p);
                }
                break;
        }
    }

    private Icon ResolveIcon(HeadsetState state)
    {
        if (state.IsInactive) return _iconHeadphone ??= LoadIcon("Headphone");

        return state.Status switch
        {
            ChargeStatus.FullyCharged => _iconGreen ??= LoadIcon("BatteryGreen"),
            ChargeStatus.Charging     => state.BatteryPercent >= 100 
                ? _iconGreen ??= LoadIcon("BatteryGreen") 
                : _iconCharging ??= LoadIcon("BatteryCharging"),
            _ => state.BatteryPercent switch
            {
                > 50 => _iconGreen ??= LoadIcon("BatteryGreen"),
                > 20 => _iconYellow ??= LoadIcon("BatteryYellow"),
                _    => _iconRed ??= LoadIcon("BatteryRed")
            }
        };
    }

    private static Icon LoadIcon(string name)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"NariMeter.{name}.ico")
            ?? throw new InvalidOperationException($"Embedded resource '{name}.ico' not found.");
        return new Icon(stream);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hwnd, IntPtr prcRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, UIntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifier.Dispose();
            UsbDevice.CloseDevice();
            _iconHeadphone?.Dispose();
            _iconGreen?.Dispose();
            _iconYellow?.Dispose();
            _iconRed?.Dispose();
            _iconCharging?.Dispose();
            _menuAnchor.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
