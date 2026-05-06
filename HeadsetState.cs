namespace NariMeter;

public enum ChargeStatus
{
    Discharging,
    Charging,
    FullyCharged,
    PoweredOff,
    Disconnected
}

public record HeadsetState(int BatteryPercent, ChargeStatus Status)
{
    public static HeadsetState Disconnected { get; } = new(0, ChargeStatus.Disconnected);
    public static HeadsetState PoweredOff   { get; } = new(0, ChargeStatus.PoweredOff);

    public static HeadsetState FromCache(int percent, ChargeStatus status) =>
        new(percent, status);

    public string TooltipLine =>
        Status switch
        {
            ChargeStatus.Disconnected => "Disconnected",
            ChargeStatus.PoweredOff   => "Powered Off",
            ChargeStatus.FullyCharged => "100% Fully Charged",
            ChargeStatus.Charging     => BatteryPercent >= 100
                ? "100% Fully Charged"
                : $"{BatteryPercent}% Charging",
            _                         => $"{BatteryPercent}%"
        };

    public bool IsInactive =>
        Status is ChargeStatus.Disconnected or ChargeStatus.PoweredOff;
}
