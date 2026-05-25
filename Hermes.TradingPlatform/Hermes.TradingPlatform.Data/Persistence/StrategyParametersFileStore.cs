using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Data.Persistence;

/// <summary>
/// File-backed store for per-strategy parameters. Saved as a single JSON map
/// keyed by StrategyId, atomically replaced on save.
/// </summary>
public sealed class StrategyParametersFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();

    public StrategyParametersFileStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesTrading");
        Directory.CreateDirectory(dir);
        FilePath = filePath ?? Path.Combine(dir, "strategy-parameters.json");
    }

    public string FilePath { get; }

    public Dictionary<string, StrategyParameters> LoadAll()
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<string, StrategyParameters>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var raw = File.ReadAllText(FilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, StrategyParameters>>(raw, JsonOptions);
            return dict is null
                ? new Dictionary<string, StrategyParameters>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, StrategyParameters>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, StrategyParameters>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveAll(IDictionary<string, StrategyParameters> parameters)
    {
        lock (_sync)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(parameters, JsonOptions));
            if (File.Exists(FilePath))
            {
                File.Replace(tmp, FilePath, null);
            }
            else
            {
                File.Move(tmp, FilePath);
            }
        }
    }

    public void Save(StrategyParameters parameters)
    {
        lock (_sync)
        {
            var dict = LoadAll();
            dict[parameters.StrategyId] = parameters;
            SaveAll(dict);
        }
    }
}
