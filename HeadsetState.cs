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

    public string TooltipLine =>
        Status switch
        {
            ChargeStatus.Disconnected => "Disconnected",
            ChargeStatus.PoweredOff   => "Powered Off",
            ChargeStatus.FullyCharged => "100% Fully Charged",
            ChargeStatus.Charging     => $"{BatteryPercent}% Charging",
            _                         => $"{BatteryPercent}%"
        };

    public bool IsInactive =>
        Status is ChargeStatus.Disconnected or ChargeStatus.PoweredOff;
}
