using System;

namespace NariMeter;

public sealed class BatteryReader
{
    private const int StabilizationTicks  = 3;
    private const int ConfirmTicks        = 2;
    private const int StepPercent         = 5;
    private const int MaxStepPerMinute    = 5;
    private const int SanityThreshold     = 40;

    private const int DefaultMinMv        = 3296;
    private const int DefaultMaxMv        = 4128;
    private const int ChargingThresholdMv = 4160;
    private const int CalibrationLowPct   = 5;
    private const int CalibrationHighPct  = 95;

    private int  _lastValidPercent;
    private int  _lastSavedPercent  = -1;
    private int  _stabilizationCounter;
    private int  _confirmCounter;
    private int  _confirmCandidate;
    private bool _stabilizing;
    private bool _hasRealReading;
    private bool _wasCharging;
    private bool _chargingJustStarted;
    private ChargeStatus _lastChargeStatus = ChargeStatus.Discharging;

    private int      _minMv;
    private int      _maxMv;
    private DateTime _lastReadTime = DateTime.UtcNow;

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

        var result = isCharging
            ? HandleCharging(mv, percentRaw)
            : HandleDischarging(mv, percentRaw);

        _lastReadTime = DateTime.UtcNow;
        return result;
    }

    private HeadsetState HandleCharging(int mv, int percentRaw)
    {
        if (mv > 0 && percentRaw > 0 && percentRaw <= 100)
            TryCalibrate(mv, percentRaw);

        if (mv >= ChargingThresholdMv)
        {
            _lastValidPercent = 100;
            _lastChargeStatus = ChargeStatus.FullyCharged;
            _hasRealReading   = true;
            SaveIfChanged(100);
            return new HeadsetState(100, ChargeStatus.FullyCharged);
        }

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

            _lastValidPercent = _confirmCandidate;
            _confirmCounter   = 0;
            _hasRealReading   = true;
            _lastChargeStatus = ChargeStatus.Charging;
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

        int firmwareBucket = (percentRaw > 0 && percentRaw <= 100)
            ? (percentRaw / StepPercent) * StepPercent
            : _lastValidPercent;

        if (firmwareBucket > _lastValidPercent)
            _lastValidPercent = Math.Min(_lastValidPercent + StepPercent, firmwareBucket);

        _lastChargeStatus = ChargeStatus.Charging;
        SaveIfChanged(_lastValidPercent);
        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    private HeadsetState HandleDischarging(int mv, int percentRaw)
    {
        if (mv > 0 && percentRaw > 0 && percentRaw <= 100)
            TryCalibrate(mv, percentRaw);

        int mvCalculated = CalculateDischargingPercent(mv);
        int targetBucket = MvToBucket(mvCalculated, 100);

        if (percentRaw > 0 && percentRaw <= 100)
        {
            int firmwareBucket = MvToBucket(percentRaw, 100);
            if (Math.Abs(firmwareBucket - targetBucket) > SanityThreshold)
                targetBucket = _hasRealReading ? MvToBucket(_lastValidPercent, 100) : firmwareBucket;
        }

        if (!_hasRealReading)
        {
            if (_confirmCounter == 0 || Math.Abs(targetBucket - _confirmCandidate) > StepPercent)
            {
                _confirmCandidate = targetBucket;
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
            _hasRealReading   = true;
            _lastChargeStatus = ChargeStatus.Discharging;
            SaveIfChanged(_lastValidPercent);
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        if (targetBucket != _lastValidPercent)
        {
            int maxSteps     = ComputeMaxSteps();
            int stepsNeeded  = Math.Abs(targetBucket - _lastValidPercent) / StepPercent;
            int stepsToApply = Math.Min(stepsNeeded, maxSteps);

            if (targetBucket < _lastValidPercent)
                _lastValidPercent -= stepsToApply * StepPercent;
            else
                _lastValidPercent += stepsToApply * StepPercent;

            _lastValidPercent = Math.Clamp(_lastValidPercent, 0, 100);
        }

        _lastChargeStatus = ChargeStatus.Discharging;
        SaveIfChanged(_lastValidPercent);
        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    private int ComputeMaxSteps()
    {
        double minutesElapsed = (DateTime.UtcNow - _lastReadTime).TotalMinutes;
        double stepsAllowed   = minutesElapsed * MaxStepPerMinute / StepPercent;
        return Math.Max(1, (int)Math.Ceiling(stepsAllowed));
    }

    private static int MvToBucket(int percent, int max) =>
        (Math.Clamp(percent, 0, max) / StepPercent) * StepPercent;

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

    private void SaveIfChanged(int percent)
    {
        if (percent == _lastSavedPercent) return;
        _lastSavedPercent = percent;
        StateStore.SavePercent(percent);
    }
}
