using System;
using System.Reflection;
using System.Runtime;
using System.Windows.Forms;

namespace NariMeter;

public sealed class TrayApp : ApplicationContext
{
    private const int StateIntervalMs   = 2000;
    private const int BatteryIntervalMs = 30000;
    private const int ActiveThreshold   = 4;

    private readonly NotifyIcon    _tray;
    private readonly BatteryReader _reader;
    private readonly System.Windows.Forms.Timer _stateTimer;
    private readonly System.Windows.Forms.Timer _batteryTimer;

    private readonly Icon _iconHeadphone;
    private readonly Icon _iconGreen;
    private readonly Icon _iconYellow;
    private readonly Icon _iconRed;
    private readonly Icon _iconCharging;

    private HeadsetState _lastState    = HeadsetState.Disconnected;
    private bool         _initialized  = false;
    private int          _activeConfirm = 0;

    public TrayApp()
    {
        _iconHeadphone = LoadIcon("Headphone");
        _iconGreen     = LoadIcon("BatteryGreen");
        _iconYellow    = LoadIcon("BatteryYellow");
        _iconRed       = LoadIcon("BatteryRed");
        _iconCharging  = LoadIcon("BatteryCharging");

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

        CompactHeap();
    }

    private static void CompactHeap()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
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
                _lastState = state;
                UpdateTray(state);
            }
        }
        else
        {
            _activeConfirm = 0;
            if (!_lastState.IsInactive)
            {
                _lastState = state;
                UpdateTray(state);
            }
        }
    }

    private void OnBatteryTick(object? sender, EventArgs e)
    {
        if (_lastState.IsInactive) return;

        var state = _reader.PollBattery();
        if (state.IsInactive) return;

        _lastState = state;
        UpdateTray(state);
    }

    private void UpdateTray(HeadsetState state)
    {
        _tray.Icon = ResolveIcon(state);
        _tray.Text = ResolveTooltip(state);
    }

    private Icon ResolveIcon(HeadsetState state)
    {
        if (state.IsInactive) return _iconHeadphone;

        if (state.Status is ChargeStatus.Charging or ChargeStatus.FullyCharged)
            return _iconCharging;

        return state.BatteryPercent switch
        {
            > 50 => _iconGreen,
            > 20 => _iconYellow,
            _    => _iconRed
        };
    }

    private static string ResolveTooltip(HeadsetState state) =>
        state.Status switch
        {
            ChargeStatus.Disconnected => "Disconnected",
            ChargeStatus.PoweredOff   => "Powered Off",
            ChargeStatus.FullyCharged => "100% Fully Charged",
            ChargeStatus.Charging     => $"{state.BatteryPercent}% Charging",
            _                         => $"{state.BatteryPercent}%"
        };

    private static Icon LoadIcon(string name)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"NariMeter.{name}.ico")
            ?? throw new InvalidOperationException($"Embedded resource '{name}.ico' not found.");
        return new Icon(stream);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu    = new ContextMenuStrip();
        var startup = new ToolStripMenuItem("Run at Startup")
        {
            Checked        = StartupManager.IsEnabled(),
            CheckOnClick   = true
        };
        startup.Click += (_, _) =>
        {
            if (startup.Checked)
                StartupManager.Enable();
            else
                StartupManager.Disable();
        };

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) =>
        {
            _tray.Visible = false;
            Application.Exit();
        };

        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
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
