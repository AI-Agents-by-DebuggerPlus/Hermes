using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.DensityHeatmap;

public sealed class DensitySnapshotDto
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "";

    [JsonPropertyName("market")]
    public string Market { get; set; } = "spot";

    [JsonPropertyName("current_price")]
    public double? CurrentPrice { get; set; }

    [JsonPropertyName("generated_at")]
    public double GeneratedAt { get; set; }

    [JsonPropertyName("levels")]
    public List<DensityLevelDto> Levels { get; set; } = [];
}

public sealed class DensityLevelDto
{
    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("side")]
    public string Side { get; set; } = "";

    [JsonPropertyName("volume")]
    public double Volume { get; set; }

    [JsonPropertyName("strength")]
    public double Strength { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("distance_pct")]
    public double? DistancePct { get; set; }

    [JsonPropertyName("eaten_ratio")]
    public double EatenRatio { get; set; }
}

public static class DensitySnapshotIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string DefaultSnapshotPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesDensity",
            "bridge",
            "density_snapshot.json");

    public static string DefaultHeartbeatPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesDensity",
            "bridge",
            "heartbeat.txt");

    public static DensitySnapshotDto? TryRead(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DensitySnapshotDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static double? HeartbeatAgeSeconds()
    {
        var path = DefaultHeartbeatPath();
        if (!File.Exists(path))
        {
            return null;
        }

        return (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds;
    }
}
