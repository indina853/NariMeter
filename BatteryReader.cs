using System;

namespace NariMeter;

public sealed class BatteryReader
{
    private const int ConfirmTicks          = 2;
    private const int RechargeConfirmTicks  = 2;
    private const int StepPercent           = 5;
    private const int StepIntervalSeconds   = 30;
    private const int SanityThreshold       = 40;
    private const int MaxChargingPercent    = 95;

    private const int StaleCacheMinutes   = 30;
    private const int CalibrationLowPct   = 5;
    private const int CalibrationHighPct  = 95;

    private static readonly TimeSpan StabilizationHold    = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ChargingConfirmHold  = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DischargeConfirmHold = TimeSpan.FromSeconds(45);

    private int      _lastValidPercent;
    private int      _lastSavedPercent         = -1;
    private int      _confirmCounter;
    private int      _confirmCandidate;
    private int      _chargingConfirmCandidate = -1;
    private DateTime _chargingCandidateSince;
    private DateTime _dischargeSince           = DateTime.MaxValue;
    private DateTime _stabilizeUntil           = DateTime.MinValue;
    private DateTime _lastStepTime             = DateTime.UtcNow;
    private bool     _hasRealReading;
    private bool     _wasCharging;
    private bool     _chargingJustStarted;
    private bool     _fullyCharged;
    private int      _rechargeConfirmTicks;
    private ChargeStatus _lastChargeStatus     = ChargeStatus.Discharging;

    private int _minMv;
    private int _maxMv;

    public bool NeedsFirstReading => !_hasRealReading;

    public BatteryReader()
    {
        _lastValidPercent = StateStore.LoadLastPercent();
        _lastSavedPercent = _lastValidPercent;

        bool cacheIsFresh = DateTime.UtcNow - StateStore.LoadLastPercentUtc()
                            < TimeSpan.FromMinutes(StaleCacheMinutes);

        _hasRealReading   = _lastValidPercent > 0 && cacheIsFresh;
        _minMv            = StateStore.LoadMinMv();
        _maxMv            = StateStore.LoadMaxMv();
        _fullyCharged     = _hasRealReading && _lastValidPercent >= 100;
    }

    public HeadsetState Poll()
    {
        if (!UsbDevice.TryRead(out int mv, out bool poweredOn, out bool isCharging, out bool isFullyCharged, out int percentRaw))
            return HeadsetState.Disconnected;

        if (!poweredOn) return HeadsetState.PoweredOff;

        if (isFullyCharged)
        {
            _lastValidPercent = 100;
            _lastChargeStatus = ChargeStatus.FullyCharged;
            _hasRealReading   = true;
            _fullyCharged     = true;
            _wasCharging      = false;
            _dischargeSince   = DateTime.MaxValue;
            SaveIfChanged(100);
            return new HeadsetState(100, ChargeStatus.FullyCharged);
        }

        var now = DateTime.UtcNow;

        if (now < _stabilizeUntil)
        {
            if (isCharging)
            {
                if (++_rechargeConfirmTicks >= RechargeConfirmTicks)
                {
                    _stabilizeUntil       = DateTime.MinValue;
                    _rechargeConfirmTicks = 0;
                    _dischargeSince       = DateTime.MaxValue;
                    _wasCharging          = true;
                    _chargingJustStarted  = true;
                    return HandleCharging(percentRaw, now);
                }
            }
            else
            {
                _rechargeConfirmTicks = 0;
            }

            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        if (isCharging)
        {
            _dischargeSince = DateTime.MaxValue;

            if (!_wasCharging)
            {
                _chargingJustStarted      = true;
                _chargingConfirmCandidate = -1;
            }
            _wasCharging = true;
        }
        else
        {
            if (_dischargeSince == DateTime.MaxValue)
                _dischargeSince = now;

            if (now - _dischargeSince >= DischargeConfirmHold)
            {
                _fullyCharged = false;
                _wasCharging  = false;
            }
        }

        return isCharging
            ? HandleCharging(percentRaw, now)
            : HandleDischarging(mv, percentRaw, now);
    }

    private HeadsetState HandleCharging(int percentRaw, DateTime now)
    {
        if (!_hasRealReading)
        {
            _chargingJustStarted = false;
            return BootstrapCharging(percentRaw);
        }

        if (_chargingJustStarted)
        {
            _chargingJustStarted = false;
            _lastChargeStatus    = ChargeStatus.Charging;
            SaveIfChanged(_lastValidPercent);
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        int firmwareBucket = (percentRaw > 0 && percentRaw < 100)
            ? (percentRaw / StepPercent) * StepPercent
            : -1;

        if (firmwareBucket > _lastValidPercent && firmwareBucket <= MaxChargingPercent)
        {
            if (firmwareBucket != _chargingConfirmCandidate)
            {
                _chargingConfirmCandidate = firmwareBucket;
                _chargingCandidateSince   = now;
            }
            else if (now - _chargingCandidateSince >= ChargingConfirmHold)
            {
                _lastValidPercent         = Math.Min(_lastValidPercent + StepPercent, _chargingConfirmCandidate);
                _chargingConfirmCandidate = -1;
            }
        }
        else if (firmwareBucket <= _lastValidPercent)
        {
            _chargingConfirmCandidate = -1;
        }

        _lastChargeStatus = ChargeStatus.Charging;
        SaveIfChanged(_lastValidPercent);
        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    private HeadsetState BootstrapCharging(int percentRaw)
    {
        int bucket;
        if (percentRaw >= 100)
            bucket = 100;
        else if (percentRaw > 0)
            bucket = (percentRaw / StepPercent) * StepPercent;
        else
            bucket = -1;

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

    private HeadsetState HandleDischarging(int mv, int percentRaw, DateTime now)
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

            if (_lastValidPercent >= 100)
                _fullyCharged = true;

            SaveIfChanged(_lastValidPercent);
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        if (_fullyCharged && targetBucket < 100)
        {
            _lastChargeStatus = ChargeStatus.Discharging;
            return new HeadsetState(100, ChargeStatus.Discharging);
        }

        if (targetBucket != _lastValidPercent)
        {
            int maxSteps     = (int)((now - _lastStepTime).TotalSeconds / StepIntervalSeconds);
            int stepsNeeded  = Math.Abs(targetBucket - _lastValidPercent) / StepPercent;
            int stepsToApply = Math.Min(stepsNeeded, maxSteps);

            if (stepsToApply > 0)
            {
                if (targetBucket < _lastValidPercent)
                    _lastValidPercent -= stepsToApply * StepPercent;
                else
                    _lastValidPercent += stepsToApply * StepPercent;

                _lastValidPercent = Math.Clamp(_lastValidPercent, 0, 100);
                _lastStepTime     = now;

                if (_lastValidPercent >= 100)
                    _fullyCharged = true;
            }
        }

        _lastChargeStatus = ChargeStatus.Discharging;
        SaveIfChanged(_lastValidPercent);
        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    private static int MvToBucket(int percent, int max) =>
        (Math.Clamp(percent, 0, max) / StepPercent) * StepPercent;

    public void NotifyCableRemoved()
    {
        _stabilizeUntil       = DateTime.UtcNow + StabilizationHold;
        _rechargeConfirmTicks = 0;
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
