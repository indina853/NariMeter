using System;
using System.IO;
using System.Text.Json;

namespace NariMeter;

public static class StateStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "NariMeter.state.json");

    private record PersistedState(int LastValidPercent);

    public static int LoadLastPercent()
    {
        try
        {
            if (!File.Exists(FilePath)) return 50;
            var json = File.ReadAllText(FilePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json);
            return state?.LastValidPercent ?? 50;
        }
        catch
        {
            return 50;
        }
    }

    public static void SavePercent(int percent)
    {
        try
        {
            var json = JsonSerializer.Serialize(new PersistedState(percent));
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
