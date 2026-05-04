using System;

namespace NariMeter;

public sealed class BatteryReader
{
    private const int StabilizationTicks = 3;
    private const int ConfirmTicks       = 2;
    private const int StepPercent        = 5;

    private const int DefaultMinMv        = 3296;
    private const int DefaultMaxMv        = 4128;
    private const int ChargingThresholdMv = 4160;
    private const int CalibrationLowPct   = 5;
    private const int CalibrationHighPct  = 95;
    private const int SanityThreshold     = 40;

    private int  _lastValidPercent;
    private int  _lastSavedPercent = -1;
    private int  _stabilizationCounter;
    private int  _confirmCounter;
    private int  _confirmCandidate;
    private bool _stabilizing;
    private bool _hasRealReading;
    private bool _wasCharging;
    private bool _chargingJustStarted;
    private ChargeStatus _lastChargeStatus = ChargeStatus.Discharging;

    private int _minMv;
    private int _maxMv;

    public bool NeedsFirstReading => !_hasRealReading;

    public BatteryReader()
    {
        _lastValidPercent = StateStore.LoadLastPercent();
        _lastSavedPercent = _lastValidPercent;
        _hasRealReading   = _lastValidPercent > 0;
        _minMv            = StateStore.LoadMinMv();
        _maxMv            = StateStore.LoadMaxMv();
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

        if (isCharging && !_wasCharging)
            _chargingJustStarted = true;

        _wasCharging = isCharging;

        if (isCharging)
            return HandleCharging(mv, percentRaw);

        return HandleDischarging(mv, percentRaw);
    }

    private HeadsetState HandleCharging(int mv, int percentRaw)
    {
        if (mv > 0 && percentRaw > 0 && percentRaw <= 100)
            TryCalibrate(mv, percentRaw);

        if (!_hasRealReading)
        {
            _chargingJustStarted = false;

            int bucket = (percentRaw > 0 && percentRaw <= 100)
                ? (percentRaw / StepPercent) * StepPercent
                : -1;

            if (bucket < 0)
                return new HeadsetState(0, ChargeStatus.Charging);

            if (_confirmCounter == 0 || Math.Abs(bucket - _confirmCandidate) > StepPercent)
            {
                _confirmCandidate = bucket;
                _confirmCounter   = 1;
            }
            else
            {
                _confirmCounter++;
            }

            if (_confirmCounter < ConfirmTicks)
                return new HeadsetState(0, ChargeStatus.Charging);

            _lastValidPercent    = _confirmCandidate;
            _confirmCounter      = 0;
            _hasRealReading      = true;
            _lastChargeStatus    = ChargeStatus.Charging;
            SaveIfChanged(_lastValidPercent);
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        if (_chargingJustStarted)
        {
            _chargingJustStarted = false;
            _lastChargeStatus    = ChargeStatus.Charging;
            SaveIfChanged(_lastValidPercent);
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        int target = CalculateChargingPercent(mv);

        if (target >= 100)
        {
            _lastValidPercent = 100;
            _lastChargeStatus = ChargeStatus.FullyCharged;
            _hasRealReading   = true;
            SaveIfChanged(100);
            return new HeadsetState(100, ChargeStatus.FullyCharged);
        }

        int targetBucket = (Math.Clamp(target, 0, 99) / StepPercent) * StepPercent;

        if (targetBucket > _lastValidPercent)
            _lastValidPercent = Math.Min(_lastValidPercent + StepPercent, targetBucket);
        else if (targetBucket < _lastValidPercent)
            _lastValidPercent = Math.Max(_lastValidPercent - StepPercent, targetBucket);

        _lastChargeStatus = ChargeStatus.Charging;
        SaveIfChanged(_lastValidPercent);
        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    private HeadsetState HandleDischarging(int mv, int percentRaw)
    {
        if (mv > 0 && percentRaw > 0 && percentRaw <= 100)
            TryCalibrate(mv, percentRaw);

        int firmwareBucket = (percentRaw > 0 && percentRaw <= 100)
            ? (percentRaw / StepPercent) * StepPercent
            : -1;

        int mvCalculated = CalculateDischargingPercent(mv);

        int target = firmwareBucket >= 0 ? firmwareBucket : mvCalculated;

        if (firmwareBucket >= 0 && mv > 0 &&
            Math.Abs(mvCalculated - firmwareBucket) > SanityThreshold)
            target = _hasRealReading ? _lastValidPercent : firmwareBucket;

        if (!_hasRealReading)
        {
            int bucket = (target / StepPercent) * StepPercent;
            if (_confirmCounter == 0 || Math.Abs(bucket - _confirmCandidate) > StepPercent)
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
            int targetBucket = (Math.Clamp(target, 0, 100) / StepPercent) * StepPercent;

            if (targetBucket < _lastValidPercent)
                _lastValidPercent = Math.Max(_lastValidPercent - StepPercent, targetBucket);
            else if (targetBucket > _lastValidPercent)
                _lastValidPercent = Math.Min(_lastValidPercent + StepPercent, targetBucket);
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

    private void TryCalibrate(int mv, int pct)
    {
        if (pct <= CalibrationLowPct && mv < _minMv)
        {
            _minMv = mv;
            StateStore.SaveMinMv(_minMv);
        }

        if (pct >= CalibrationHighPct && mv > _maxMv)
        {
            _maxMv = mv;
            StateStore.SaveMaxMv(_maxMv);
        }
    }

    private int CalculateDischargingPercent(int mv)
    {
        if (mv <= 0) return _lastValidPercent;
        mv = Math.Clamp(mv, _minMv, _maxMv);
        double t = (double)(mv - _minMv) / (_maxMv - _minMv);
        return (int)Math.Round(t * 100);
    }

    private int CalculateChargingPercent(int mv)
    {
        if (mv <= 0) return _lastValidPercent;
        if (mv >= ChargingThresholdMv) return 100;
        mv = Math.Clamp(mv, _minMv, ChargingThresholdMv);
        double t = (double)(mv - _minMv) / (ChargingThresholdMv - _minMv);
        return (int)Math.Round(t * 100);
    }

    private void SaveIfChanged(int percent)
    {
        if (percent == _lastSavedPercent) return;
        _lastSavedPercent = percent;
        StateStore.SavePercent(percent);
    }
}
