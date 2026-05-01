using System;
using System.Reflection;
using System.Windows.Forms;

namespace NariMeter;

public sealed class TrayApp : ApplicationContext
{
    private const int StateIntervalMs        = 2000;
    private const int BatteryIntervalMs      = 30000;
    private const int DisconnectedIntervalMs = 500;
    private const int ActiveThreshold        = 4;

    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromMinutes(10);

    private readonly NotifyIcon    _tray;
    private readonly BatteryReader _reader;
    private readonly System.Windows.Forms.Timer _stateTimer;
    private readonly System.Windows.Forms.Timer _batteryTimer;

    private readonly Icon _iconHeadphone;
    private readonly Icon _iconGreen;
    private readonly Icon _iconYellow;
    private readonly Icon _iconRed;
    private readonly Icon _iconCharging;

    private HeadsetState _lastState          = HeadsetState.Disconnected;
    private bool         _initialized        = false;
    private int          _activeConfirm      = 0;
    private bool         _notifiedWarn       = false;
    private bool         _notifiedCrit       = false;
    private bool         _notifiedCharged    = false;
    private bool         _notificationsEnabled;
    private int          _cachedPercent      = 50;
    private ChargeStatus _cachedStatus       = ChargeStatus.Discharging;
    private DateTime     _lastSuccessfulRead = DateTime.UtcNow;
    private int          _lowBatteryWarn;
    private int          _lowBatteryCrit;

    private ToolStripMenuItem _notifyToggle = null!;

    public TrayApp()
    {
        _iconHeadphone = LoadIcon("Headphone");
        _iconGreen     = LoadIcon("BatteryGreen");
        _iconYellow    = LoadIcon("BatteryYellow");
        _iconRed       = LoadIcon("BatteryRed");
        _iconCharging  = LoadIcon("BatteryCharging");

        _notificationsEnabled = StateStore.LoadNotificationsEnabled();
        _cachedPercent        = StateStore.LoadLastPercent();
        _lowBatteryWarn       = StateStore.LoadLowBatteryWarn();
        _lowBatteryCrit       = StateStore.LoadLowBatteryCrit();

        _reader = new BatteryReader();

        _tray = new NotifyIcon
        {
            Visible          = true,
            Icon             = _iconHeadphone,
            Text             = "Disconnected",
            ContextMenuStrip = BuildMenu()
        };

        _stateTimer = new System.Windows.Forms.Timer { Interval = StateIntervalMs };
        _stateTimer.Tick += OnStateTick;
        _stateTimer.Start();

        _batteryTimer = new System.Windows.Forms.Timer { Interval = BatteryIntervalMs };
        _batteryTimer.Tick += OnBatteryTick;
        _batteryTimer.Start();
    }

    private void OnStateTick(object? sender, EventArgs e)
    {
        var state = _reader.PollState();

        if (!_initialized)
        {
            _initialized = true;
            if (state.IsInactive)
            {
                _lastState = state;
                UpdateTray(state);
            }
            return;
        }

        if (!state.IsInactive)
        {
            _activeConfirm++;
            if (_activeConfirm < ActiveThreshold) return;

            if (_lastState.IsInactive)
            {
                _lastState           = state;
                _stateTimer.Interval = StateIntervalMs;
                UpdateTray(state);
            }
        }
        else
        {
            _activeConfirm = 0;

            if (!_lastState.IsInactive || _lastState.Status != state.Status)
            {
                _lastState           = state;
                _stateTimer.Interval = DisconnectedIntervalMs;
                ResetNotificationFlags();
                UpdateTray(state);
            }
        }
    }

    private void OnBatteryTick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastSuccessfulRead > StalenessThreshold)
        {
            _lastSuccessfulRead = DateTime.UtcNow;
            OnBatteryTick(null, EventArgs.Empty);
            return;
        }

        if (_lastState.IsInactive) return;

        var state = _reader.PollBattery();

        if (state.BatteryPercent == 0 && !_lastState.IsInactive)
        {
            UpdateTray(HeadsetState.FromCache(_cachedPercent, _cachedStatus));
            return;
        }

        if (state.IsInactive) return;

        if (state.BatteryPercent > 0)
        {
            _cachedPercent      = state.BatteryPercent;
            _cachedStatus       = state.Status;
            _lastSuccessfulRead = DateTime.UtcNow;
        }

        var previous = _lastState;
        _lastState = state;
        UpdateTray(state);
        CheckNotifications(previous, state);
    }

    private void CheckNotifications(HeadsetState previous, HeadsetState current)
    {
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

    private Icon ResolveIcon(HeadsetState state)
    {
        if (state.IsInactive) return _iconHeadphone;

        return state.Status switch
        {
            ChargeStatus.FullyCharged => _iconGreen,
            ChargeStatus.Charging     => _iconCharging,
            _ => state.BatteryPercent switch
            {
                > 50 => _iconGreen,
                > 20 => _iconYellow,
                _    => _iconRed
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

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _notifyToggle = new ToolStripMenuItem("Show Notifications")
        {
            Checked      = _notificationsEnabled,
            CheckOnClick = true
        };
        _notifyToggle.Click += (_, _) =>
        {
            _notificationsEnabled = _notifyToggle.Checked;
            StateStore.SaveNotificationsEnabled(_notificationsEnabled);
        };

        var startup = new ToolStripMenuItem("Run at Startup")
        {
            Checked      = StartupManager.IsEnabled(),
            CheckOnClick = true
        };
        startup.Click += (_, _) =>
        {
            if (startup.Checked) StartupManager.Enable();
            else StartupManager.Disable();
        };

        var warnMenu = new ToolStripMenuItem("Warn Threshold");
        var critMenu = new ToolStripMenuItem("Crit Threshold");

        foreach (var pct in new[] { 10, 15, 20, 25, 30 })
        {
            var p = pct;
            var item = new ToolStripMenuItem($"{p}%") { Tag = p };
            item.Click += (_, _) =>
            {
                if (p <= _lowBatteryCrit)
                {
                    _lowBatteryCrit = p - 5 < 5 ? 5 : p - 5;
                    StateStore.SaveLowBatteryCrit(_lowBatteryCrit);
                    RefreshThresholdMenu(critMenu, _lowBatteryCrit);
                }
                _lowBatteryWarn = p;
                StateStore.SaveLowBatteryWarn(p);
                RefreshThresholdMenu(warnMenu, p);
            };
            item.Checked = p == _lowBatteryWarn;
            warnMenu.DropDownItems.Add(item);
        }

        foreach (var pct in new[] { 5, 10, 15 })
        {
            var p = pct;
            var item = new ToolStripMenuItem($"{p}%") { Tag = p };
            item.Click += (_, _) =>
            {
                if (p >= _lowBatteryWarn)
                {
                    _lowBatteryWarn = p + 5 > 30 ? 30 : p + 5;
                    StateStore.SaveLowBatteryWarn(_lowBatteryWarn);
                    RefreshThresholdMenu(warnMenu, _lowBatteryWarn);
                }
                _lowBatteryCrit = p;
                StateStore.SaveLowBatteryCrit(p);
                RefreshThresholdMenu(critMenu, p);
            };
            item.Checked = p == _lowBatteryCrit;
            critMenu.DropDownItems.Add(item);
        }

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            _tray.Visible = false;
            Application.Exit();
        };

        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_notifyToggle);
        menu.Items.Add(warnMenu);
        menu.Items.Add(critMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private static void RefreshThresholdMenu(ToolStripMenuItem menu, int selected)
    {
        foreach (ToolStripMenuItem item in menu.DropDownItems)
            item.Checked = (int)item.Tag! == selected;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stateTimer.Dispose();
            _batteryTimer.Dispose();
            _iconHeadphone.Dispose();
            _iconGreen.Dispose();
            _iconYellow.Dispose();
            _iconRed.Dispose();
            _iconCharging.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
