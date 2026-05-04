using System;
using System.IO;
using System.Text.Json;

namespace NariMeter;

public static class StateStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "NariMeter.state.json");

    private static readonly string TempPath =
        Path.Combine(AppContext.BaseDirectory, "NariMeter.state.tmp");

    private const int DefaultLowBatteryWarn = 20;
    private const int DefaultLowBatteryCrit = 10;

    private record PersistedState(
        int  LastValidPercent,
        int  LastValidMv,
        bool NotificationsEnabled,
        int  LowBatteryWarn,
        int  LowBatteryCrit);

    private static PersistedState? _cache;

    public static int  LoadLastPercent()          => Get().LastValidPercent;
    public static bool LoadNotificationsEnabled() => Get().NotificationsEnabled;
    public static int  LoadLowBatteryWarn()       => Get().LowBatteryWarn;
    public static int  LoadLowBatteryCrit()       => Get().LowBatteryCrit;

    public static void SavePercent(int percent)
    {
        Mutate(s => s with { LastValidPercent = percent, LastValidMv = 0 });
    }

    public static void SaveNotificationsEnabled(bool enabled)
    {
        Mutate(s => s with { NotificationsEnabled = enabled });
    }

    public static void SaveLowBatteryWarn(int threshold)
    {
        Mutate(s => s with { LowBatteryWarn = threshold });
    }

    public static void SaveLowBatteryCrit(int threshold)
    {
        Mutate(s => s with { LowBatteryCrit = threshold });
    }

    private static PersistedState Get()
    {
        if (_cache is not null) return _cache;
        _cache = Load();
        return _cache;
    }

    private static void Mutate(Func<PersistedState, PersistedState> transform)
    {
        _cache = transform(Get());
        Persist(_cache);
    }

    private static void Persist(PersistedState state)
    {
        try
        {
            File.WriteAllText(TempPath, JsonSerializer.Serialize(state));
            File.Move(TempPath, FilePath, overwrite: true);
        }
        catch { }
    }

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
        new(0, 0, false, DefaultLowBatteryWarn, DefaultLowBatteryCrit);
}
