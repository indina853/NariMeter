using System;

namespace NariMeter;

public sealed class BatteryReader
{
    private const int MinMv              = 3296;
    private const int MaxMv              = 4128;
    private const int ChargingThreshold  = 4160;
    private const int StabilizationTicks = 3;

    private int  _lastValidPercent;
    private int  _stabilizationCounter;
    private bool _stabilizing;
    private bool _initialized;

    public BatteryReader()
    {
        _lastValidPercent = StateStore.LoadLastPercent();
    }

    public HeadsetState PollState()
    {
        if (!UsbDevice.TryRead(out int mv, out bool poweredOn))
            return HeadsetState.Disconnected;

        if (!_initialized)
        {
            _initialized = true;
            if (!poweredOn || mv == 0)
                return HeadsetState.PoweredOff;

            int pct = ComputePercent(mv);
            _lastValidPercent = pct;
            StateStore.SavePercent(pct);
            return new HeadsetState(pct, mv > ChargingThreshold
                ? ChargeStatus.Charging
                : ChargeStatus.Discharging);
        }

        if (!poweredOn)
            return HeadsetState.PoweredOff;

        return new HeadsetState(_lastValidPercent, mv > ChargingThreshold
            ? ChargeStatus.Charging
            : ChargeStatus.Discharging);
    }

    public HeadsetState PollBattery()
    {
        if (!UsbDevice.TryRead(out int mv, out bool poweredOn))
            return HeadsetState.Disconnected;

        if (!poweredOn) return HeadsetState.PoweredOff;

        if (_stabilizing)
        {
            _stabilizationCounter++;
            if (_stabilizationCounter < StabilizationTicks)
                return new HeadsetState(_lastValidPercent, ChargeStatus.Discharging);
            _stabilizing = false;
        }

        if (mv > ChargingThreshold)
        {
            return _lastValidPercent >= 100
                ? new HeadsetState(100, ChargeStatus.FullyCharged)
                : new HeadsetState(_lastValidPercent, ChargeStatus.Charging);
        }

        int percent = ComputePercent(mv);
        _lastValidPercent = percent;
        StateStore.SavePercent(percent);
        return new HeadsetState(percent, ChargeStatus.Discharging);
    }

    public void NotifyCableRemoved()
    {
        _stabilizing          = true;
        _stabilizationCounter = 0;
    }

    private static int ComputePercent(int mv) =>
        Math.Clamp(
            (int)Math.Round((double)(mv - MinMv) / (MaxMv - MinMv) * 100 / 5) * 5,
            0, 100);
}
