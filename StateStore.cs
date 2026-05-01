using System;
using System.IO;
using System.Text.Json;

namespace NariMeter;

public static class StateStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "NariMeter.state.json");

    private const int DefaultLowBatteryWarn = 20;
    private const int DefaultLowBatteryCrit = 10;

    private record PersistedState(
        int  LastValidPercent,
        int  LastValidMv,
        bool NotificationsEnabled,
        int  LowBatteryWarn,
        int  LowBatteryCrit);

    public static int  LoadLastPercent()          => Load().LastValidPercent;
    public static bool LoadNotificationsEnabled() => Load().NotificationsEnabled;
    public static int  LoadLowBatteryWarn()       => Load().LowBatteryWarn;
    public static int  LoadLowBatteryCrit()       => Load().LowBatteryCrit;

    public static void SavePercent(int percent)
    {
        Save(Load() with { LastValidPercent = percent, LastValidMv = 0 });
    }

    public static void SaveNotificationsEnabled(bool enabled)
    {
        Save(Load() with { NotificationsEnabled = enabled });
    }

    public static void SaveLowBatteryWarn(int threshold)
    {
        Save(Load() with { LowBatteryWarn = threshold });
    }

    public static void SaveLowBatteryCrit(int threshold)
    {
        Save(Load() with { LowBatteryCrit = threshold });
    }

    private static void Save(PersistedState state) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state));

    private static PersistedState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Default();
            return JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(FilePath))
                ?? Default();
        }
        catch { return Default(); }
    }

    private static PersistedState Default() =>
        new(50, 0, false, DefaultLowBatteryWarn, DefaultLowBatteryCrit);
}
