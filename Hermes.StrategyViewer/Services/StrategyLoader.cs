using System.IO;
using System.Text.Json;
using Hermes.StrategyViewer.Models;

namespace Hermes.StrategyViewer.Services;

public static class StrategyLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static StrategyDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<StrategyDocument>(json, JsonOpts)
                  ?? throw new InvalidOperationException("Empty strategy JSON");
        return doc;
    }

    public static string? ResolveDefaultStrategyPath()
    {
        foreach (var start in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                     Path.GetDirectoryName(Environment.ProcessPath) ?? "",
                 })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var sibling = Path.GetFullPath(Path.Combine(dir.FullName, "..", "HermesProjects", "Trading Analytics"));
                var candidate = Path.Combine(sibling, "strategies", "gold_xauusd.strategy.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                var docsExample = Path.Combine(
                    dir.FullName,
                    "Docs",
                    "TradingAnalytics",
                    "examples",
                    "gold_xauusd.strategy.json");
                if (File.Exists(docsExample))
                {
                    return docsExample;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }
}
