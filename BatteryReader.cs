using System;
using System.Collections.Generic;

namespace NariMeter;

public sealed class BatteryReader
{
    private const int MinMv                = 3296;
    private const int MaxMv                = 4128;
    private const int StabilizationTicks   = 3;
    private const int TrendSamples         = 4;
    private const int TrendRisingThreshold = 8;

    private int  _lastValidPercent;
    private int  _stabilizationCounter;
    private bool _stabilizing;
    private bool _initialized;

    private readonly Queue<int> _mvHistory = new();
    private ChargeStatus _lastChargeStatus = ChargeStatus.Discharging;

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

            _mvHistory.Enqueue(mv);
            return new HeadsetState(_lastValidPercent, _lastChargeStatus);
        }

        if (!poweredOn)
            return HeadsetState.PoweredOff;

        _mvHistory.Enqueue(mv);
        if (_mvHistory.Count > TrendSamples)
            _mvHistory.Dequeue();

        _lastChargeStatus = DetectChargeStatus();

        return new HeadsetState(_lastValidPercent, _lastChargeStatus);
    }

    public HeadsetState PollBattery()
    {
        if (!UsbDevice.TryRead(out int mv, out bool poweredOn))
            return HeadsetState.Disconnected;

        if (!poweredOn) return HeadsetState.PoweredOff;

        _mvHistory.Enqueue(mv);
        if (_mvHistory.Count > TrendSamples)
            _mvHistory.Dequeue();

        _lastChargeStatus = DetectChargeStatus();

        if (_stabilizing)
        {
            _stabilizationCounter++;
            if (_stabilizationCounter < StabilizationTicks)
                return new HeadsetState(_lastValidPercent, ChargeStatus.Discharging);
            _stabilizing = false;
        }

        if (_lastChargeStatus == ChargeStatus.Charging)
        {
            return _lastValidPercent >= 100
                ? new HeadsetState(100, ChargeStatus.FullyCharged)
                : new HeadsetState(_lastValidPercent, ChargeStatus.Charging);
        }

        int percent       = ComputePercent(mv);
        _lastValidPercent = percent;
        StateStore.SavePercent(percent);
        return new HeadsetState(percent, ChargeStatus.Discharging);
    }

    public void NotifyCableRemoved()
    {
        _stabilizing          = true;
        _stabilizationCounter = 0;
        _lastChargeStatus     = ChargeStatus.Discharging;
        _mvHistory.Clear();
    }

    private ChargeStatus DetectChargeStatus()
    {
        if (_mvHistory.Count < TrendSamples)
            return _lastChargeStatus;

        var samples = _mvHistory.ToArray();
        int delta   = samples[^1] - samples[0];

        if (delta >= TrendRisingThreshold)
            return ChargeStatus.Charging;

        if (delta <= -TrendRisingThreshold)
            return ChargeStatus.Discharging;

        return _lastChargeStatus;
    }

    private static int ComputePercent(int mv) =>
        Math.Clamp(
            (int)Math.Round((double)(mv - MinMv) / (MaxMv - MinMv) * 100 / 5) * 5,
            0, 100);
}