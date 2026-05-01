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
            return poweredOn
                ? new HeadsetState(_lastValidPercent, _lastChargeStatus)
                : HeadsetState.PoweredOff;
        }
        return poweredOn
            ? new HeadsetState(_lastValidPercent, _lastChargeStatus)
            : HeadsetState.PoweredOff;
    }
    public HeadsetState PollBattery()
    {
        if (!UsbDevice.TryRead(out int mv, out bool poweredOn, out bool isCharging, out int percentRaw))
            return HeadsetState.Disconnected;
        if (!poweredOn) return HeadsetState.PoweredOff;
        if (_stabilizing)
        {
            _stabilizationCounter++;
            if (_stabilizationCounter < StabilizationTicks)
                return new HeadsetState(_lastValidPercent, _lastChargeStatus);
            _stabilizing = false;
        }
        int calculated = isCharging
            ? CalculateChargingPercent(mv)
            : CalculateDischargingPercent(mv);
        if (percentRaw > 0 && Math.Abs(calculated - percentRaw) > 50)
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        if (isCharging)
        {
            if (calculated >= 100)
            {
                _lastValidPercent = 100;
                _lastChargeStatus = ChargeStatus.FullyCharged;
                StateStore.SavePercent(100);
                return new HeadsetState(100, ChargeStatus.FullyCharged);
            }
            if (calculated < _lastValidPercent)
                calculated = _lastValidPercent;
            _lastValidPercent = Math.Clamp(calculated, 0, 99);
            _lastValidPercent = (_lastValidPercent / 5) * 5;
            _lastChargeStatus = ChargeStatus.Charging;
        }
        else
        {
            _lastValidPercent = (int)(_lastValidPercent * 0.7 + calculated * 0.3);
            _lastValidPercent = Math.Clamp(_lastValidPercent, 0, 100);
            _lastValidPercent = (_lastValidPercent / 5) * 5;
            _lastChargeStatus = ChargeStatus.Discharging;
        }
        StateStore.SavePercent(_lastValidPercent);
        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }
    public void NotifyCableRemoved()
    {
        _stabilizing          = true;
        _stabilizationCounter = 0;
        _lastChargeStatus     = ChargeStatus.Discharging;
    }
    private static int CalculateDischargingPercent(int mv)
    {
        mv = Math.Clamp(mv, MinMv, MaxMv);
        double t = (double)(mv - MinMv) / (MaxMv - MinMv);
        return (int)Math.Round(t * 100);
    }
    private static int CalculateChargingPercent(int mv)
    {
        if (mv >= ChargingThreshold)
            return 100;
        mv = Math.Clamp(mv, MinMv, ChargingThreshold);
        double t = (double)(mv - MinMv) / (ChargingThreshold - MinMv);
        return (int)Math.Round(t * 100);
    }
}
