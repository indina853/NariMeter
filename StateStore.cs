using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NariMeter;

public static class StateStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "NariMeter.state.json");

    private static readonly string TempPath =
        Path.Combine(AppContext.BaseDirectory, "NariMeter.state.tmp");

    private const int DefaultLowBatteryWarn = 20;
    private const int DefaultLowBatteryCrit = 10;
    private const int DefaultMinMv          = 3296;
    private const int DefaultMaxMv          = 4128;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private record PersistedState(
        int      LastValidPercent,
        bool     NotificationsEnabled,
        int      LowBatteryWarn,
        int      LowBatteryCrit,
        int      MinMv,
        int      MaxMv,
        DateTime LastPercentUtc = default);

    private static PersistedState? _cache;

    public static int      LoadLastPercent()      => Get().LastValidPercent;
    public static DateTime LoadLastPercentUtc()   => Get().LastPercentUtc;
    public static bool LoadNotificationsEnabled() => Get().NotificationsEnabled;
    public static int  LoadLowBatteryWarn()       => Get().LowBatteryWarn;
    public static int  LoadLowBatteryCrit()       => Get().LowBatteryCrit;
    public static int  LoadMinMv()                => Get().MinMv;
    public static int  LoadMaxMv()                => Get().MaxMv;

    public static void SavePercent(int percent)
    {
        Mutate(s => s with { LastValidPercent = percent, LastPercentUtc = DateTime.UtcNow });
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

    public static void SaveMinMv(int mv)
    {
        Mutate(s => s with { MinMv = mv });
    }

    public static void SaveMaxMv(int mv)
    {
        Mutate(s => s with { MaxMv = mv });
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
            using var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            JsonSerializer.Serialize(stream, state, JsonOptions);
            stream.Flush();
            stream.Close();
            File.Move(TempPath, FilePath, overwrite: true);
        }
        catch { }
    }

    private static PersistedState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Default();
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            var loaded = JsonSerializer.Deserialize<PersistedState>(stream, JsonOptions);
            if (loaded is null) return Default();
            return loaded with
            {
                MinMv = loaded.MinMv > 0 ? loaded.MinMv : DefaultMinMv,
                MaxMv = loaded.MaxMv > 0 && loaded.MaxMv <= DefaultMaxMv ? loaded.MaxMv : DefaultMaxMv
            };
        }
        catch { return Default(); }
    }

    private static PersistedState Default() =>
        new(0, false, DefaultLowBatteryWarn, DefaultLowBatteryCrit, DefaultMinMv, DefaultMaxMv);
}
