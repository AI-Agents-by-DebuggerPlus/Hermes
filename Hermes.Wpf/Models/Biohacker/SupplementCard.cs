using System.Globalization;
using System.Text;

namespace Hermes.Wpf.Models.Biohacker;

/// <summary>One supplement / nootropic card, persisted to Health/Supplements/{name}.md.</summary>
public sealed class SupplementCard
{
    public string Name { get; set; } = string.Empty;

    /// <summary>mineral | vitamin | nootropic | adaptogen | amino | other.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>active | paused | finished | out_of_stock.</summary>
    public string Status { get; set; } = "active";

    public int DoseMg { get; set; }
    public string DoseUnit { get; set; } = "mg";

    /// <summary>morning | afternoon | evening | before_sleep | with_meal | fasted.</summary>
    public string Timing { get; set; } = string.Empty;

    public string Frequency { get; set; } = "daily";

    public int StockUnits { get; set; }
    public int StockDaysLeft { get; set; }
    public int ReorderThreshold { get; set; } = 14;

    public List<string> ObservedEffects { get; set; } = new();
    public string StackCompatibility { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string SourceFile { get; set; } = string.Empty;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: supplement_card");
        sb.AppendLine("role: Biohacker");
        sb.AppendLine("tags: [supplement, health, biohacking]");
        sb.AppendLine("importance: 3");
        sb.Append("name: ").Append('"').Append(BiohackerYaml.YamlString(Name)).Append('"').AppendLine();
        sb.Append("category: ").AppendLine(Category);
        sb.Append("status: ").AppendLine(Status);
        sb.Append("dose_mg: ").Append(DoseMg.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("dose_unit: ").AppendLine(DoseUnit);
        sb.Append("timing: ").AppendLine(Timing);
        sb.Append("frequency: ").AppendLine(Frequency);
        sb.Append("stock_units: ").Append(StockUnits.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("stock_days_left: ").Append(StockDaysLeft.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("reorder_threshold: ").Append(ReorderThreshold.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("observed_effects: ").AppendLine(BiohackerYaml.YamlList(ObservedEffects));
        sb.Append("stack_compatibility: ").Append('"').Append(BiohackerYaml.YamlString(StackCompatibility)).Append('"').AppendLine();
        sb.Append("last_updated: ").AppendLine(BiohackerYaml.IsoUtc(LastUpdated));
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# ").Append(string.IsNullOrWhiteSpace(Name) ? "Supplement" : Name).AppendLine();
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(Notes))
        {
            sb.AppendLine(Notes.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## Эффекты");
        foreach (var e in ObservedEffects)
        {
            sb.Append("- ").AppendLine(e);
        }

        if (!string.IsNullOrWhiteSpace(StackCompatibility))
        {
            sb.AppendLine();
            sb.AppendLine("## Совместимость со стеком");
            sb.AppendLine(StackCompatibility.Trim());
        }

        return sb.ToString();
    }

    public static SupplementCard? FromMemoryItem(MemoryItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.RawMarkdown))
        {
            return null;
        }

        var y = BiohackerYaml.ReadFrontmatter(item.RawMarkdown);
        var type = BiohackerYaml.Str(y, "type");
        if (type.Length > 0 && !string.Equals(type, "supplement_card", StringComparison.OrdinalIgnoreCase))
        {
            // Allow files that lack explicit type but live in Health/Supplements/.
            if (!item.SourceFile.Replace('\\', '/').Contains("Health/Supplements/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var name = BiohackerYaml.Str(y, "name");
        if (name.Length == 0)
        {
            name = System.IO.Path.GetFileNameWithoutExtension(item.SourceFile);
            if (string.Equals(name, "README", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var card = new SupplementCard
        {
            Name = name,
            Category = BiohackerYaml.Str(y, "category"),
            Status = BiohackerYaml.Str(y, "status"),
            DoseMg = BiohackerYaml.Int(y, "dose_mg"),
            DoseUnit = BiohackerYaml.Str(y, "dose_unit"),
            Timing = BiohackerYaml.Str(y, "timing"),
            Frequency = BiohackerYaml.Str(y, "frequency"),
            StockUnits = BiohackerYaml.Int(y, "stock_units"),
            StockDaysLeft = BiohackerYaml.Int(y, "stock_days_left"),
            ReorderThreshold = BiohackerYaml.Int(y, "reorder_threshold", 14),
            ObservedEffects = BiohackerYaml.List(y, "observed_effects"),
            StackCompatibility = BiohackerYaml.Str(y, "stack_compatibility"),
            LastUpdated = BiohackerYaml.DateTimeUtc(y, "last_updated", DateTime.UtcNow),
            SourceFile = item.SourceFile,
            Notes = item.Content?.Trim() ?? string.Empty,
        };

        if (string.IsNullOrWhiteSpace(card.Status))
        {
            card.Status = "active";
        }

        if (string.IsNullOrWhiteSpace(card.DoseUnit))
        {
            card.DoseUnit = "mg";
        }

        return card;
    }
}
