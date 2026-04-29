using System;

namespace NariMeter;

public sealed class BatteryReader
{
    private const int StabilizationTicks = 3;

    private int  _lastValidPercent;
    private int  _stabilizationCounter;
    private bool _stabilizing;
    private bool _initialized;

    private ChargeStatus _lastChargeStatus = ChargeStatus.Discharging;

    public BatteryReader()
    {
        _lastValidPercent = StateStore.LoadLastPercent();
    }

    public HeadsetState PollState()
    {
        if (!UsbDevice.TryRead(out _, out bool poweredOn, out _, out _))
            return HeadsetState.Disconnected;

        if (!_initialized)
        {
            _initialized = true;
            if (!poweredOn)
                return HeadsetState.PoweredOff;

            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        if (!poweredOn)
            return HeadsetState.PoweredOff;

        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    public HeadsetState PollBattery()
    {
        if (!UsbDevice.TryRead(out _, out bool poweredOn, out bool isCharging, out int percent))
            return HeadsetState.Disconnected;

        if (!poweredOn) return HeadsetState.PoweredOff;

        if (_stabilizing)
        {
            _stabilizationCounter++;
            if (_stabilizationCounter < StabilizationTicks)
                return new HeadsetState(_lastValidPercent, ChargeStatus.Discharging);
            _stabilizing = false;
        }

        if (isCharging)
        {
            _lastValidPercent = percent;
            StateStore.SavePercent(percent);
            _lastChargeStatus = percent >= 100
                ? ChargeStatus.FullyCharged
                : ChargeStatus.Charging;
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        _lastValidPercent = percent;
        _lastChargeStatus = ChargeStatus.Discharging;
        StateStore.SavePercent(percent);
        return new HeadsetState(percent, ChargeStatus.Discharging);
    }

    public void NotifyCableRemoved()
    {
        _stabilizing          = true;
        _stabilizationCounter = 0;
        _lastChargeStatus     = ChargeStatus.Discharging;
    }
}
