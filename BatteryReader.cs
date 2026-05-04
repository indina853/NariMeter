using System;

namespace NariMeter;

public sealed class BatteryReader
{
    private const int StabilizationTicks = 3;
    private const int ConfirmTicks       = 2;

    private int  _lastValidPercent;
    private int  _lastSavedPercent = -1;
    private int  _stabilizationCounter;
    private int  _confirmCounter;
    private int  _confirmCandidate;
    private bool _stabilizing;
    private bool _hasRealReading;
    private ChargeStatus _lastChargeStatus = ChargeStatus.Discharging;

    public bool NeedsFirstReading => !_hasRealReading;

    public BatteryReader()
    {
        _lastValidPercent = StateStore.LoadLastPercent();
        _lastSavedPercent = _lastValidPercent;
        _hasRealReading   = _lastValidPercent > 0;
    }

    public HeadsetState PollState()
    {
        if (!UsbDevice.TryRead(out _, out bool poweredOn, out _, out _))
            return HeadsetState.Disconnected;

        return poweredOn
            ? new HeadsetState(_hasRealReading ? _lastValidPercent : 0, _lastChargeStatus)
            : HeadsetState.PoweredOff;
    }

    public HeadsetState PollBattery()
    {
        if (!UsbDevice.TryRead(out _, out bool poweredOn, out bool isCharging, out int percentRaw))
            return HeadsetState.Disconnected;

        if (!poweredOn) return HeadsetState.PoweredOff;

        if (_stabilizing)
        {
            _stabilizationCounter++;
            if (_stabilizationCounter < StabilizationTicks)
                return new HeadsetState(_lastValidPercent, _lastChargeStatus);
            _stabilizing = false;
        }

        int percent = (percentRaw > 0 && percentRaw <= 100)
            ? percentRaw
            : _lastValidPercent;

        if (isCharging)
        {
            if (percent >= 100)
            {
                _lastValidPercent = 100;
                _lastChargeStatus = ChargeStatus.FullyCharged;
                _hasRealReading   = true;
                SaveIfChanged(100);
                return new HeadsetState(100, ChargeStatus.FullyCharged);
            }

            if (_hasRealReading && percent < _lastValidPercent)
                percent = _lastValidPercent;

            _lastValidPercent = (Math.Clamp(percent, 0, 99) / 5) * 5;
            _lastChargeStatus = ChargeStatus.Charging;
            _hasRealReading   = true;
            SaveIfChanged(_lastValidPercent);
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        if (!_hasRealReading)
        {
            int bucket = (percent / 5) * 5;
            if (_confirmCounter == 0 || Math.Abs(bucket - _confirmCandidate) > 5)
            {
                _confirmCandidate = bucket;
                _confirmCounter   = 1;
            }
            else
            {
                _confirmCounter++;
            }

            if (_confirmCounter < ConfirmTicks)
                return new HeadsetState(0, ChargeStatus.Discharging);

            _lastValidPercent = _confirmCandidate;
            _confirmCounter   = 0;
        }
        else
        {
            int smooth        = (int)Math.Round(_lastValidPercent * 0.4 + percent * 0.6);
            _lastValidPercent = (Math.Clamp(smooth, 0, 100) / 5) * 5;
        }

        _lastChargeStatus = ChargeStatus.Discharging;
        _hasRealReading   = true;
        SaveIfChanged(_lastValidPercent);
        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    public void NotifyCableRemoved()
    {
        _stabilizing          = true;
        _stabilizationCounter = 0;
        _lastChargeStatus     = ChargeStatus.Discharging;
    }

    private void SaveIfChanged(int percent)
    {
        if (percent == _lastSavedPercent) return;
        _lastSavedPercent = percent;
        StateStore.SavePercent(percent);
    }
}
