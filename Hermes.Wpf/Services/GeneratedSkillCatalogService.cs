using System.IO;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class GeneratedSkillCatalogService
{
    private readonly Func<HermesSettings> _settings;
    private readonly object _lock = new();
    private List<GeneratedSkillManifest> _skills = [];

    public GeneratedSkillCatalogService(Func<HermesSettings> settings) => _settings = settings;

    public event Action? CatalogChanged;

    public IReadOnlyList<GeneratedSkillManifest> Skills
    {
        get
        {
            lock (_lock)
            {
                return _skills;
            }
        }
    }

    public void Reload()
    {
        var root = GeneratedSkillPaths.ResolveWindowsSkillsRoot(_settings());
        var list = new List<GeneratedSkillManifest>();
        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = GeneratedSkillPaths.ManifestPath(dir);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var item = TryLoadManifest(manifestPath, dir);
                if (item is not null)
                {
                    list.Add(item);
                }
            }
        }

        list = list
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        lock (_lock)
        {
            _skills = list;
        }

        CatalogChanged?.Invoke();
    }

    public IReadOnlyList<AgentSkillCard> AllCards()
    {
        var generated = Skills
            .Where(s => s.Enabled)
            .Select(s => new AgentSkillCard
            {
                Category = s.Category,
                Title = s.Title,
                Summary = string.IsNullOrWhiteSpace(s.Summary)
                    ? $"Generated skill `{s.Id}` ({s.Kind})"
                    : s.Summary,
            })
            .ToList();

        if (generated.Count == 0)
        {
            return AgentSkillsCatalog.All;
        }

        var merged = new List<AgentSkillCard>(AgentSkillsCatalog.All.Count + generated.Count);
        merged.AddRange(AgentSkillsCatalog.All);
        merged.AddRange(generated);
        return merged;
    }

    public IEnumerable<string> OutboundPromptBlocks()
    {
        foreach (var skill in Skills.Where(s => s.Enabled))
        {
            if (string.IsNullOrWhiteSpace(skill.OutboundPromptBlock))
            {
                continue;
            }

            yield return $"### Generated skill: {skill.Title} ({skill.Id})\n{skill.OutboundPromptBlock.Trim()}";
        }
    }

    public GeneratedSkillManifest? MatchTrigger(string userMessage)
    {
        var text = (userMessage ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        GeneratedSkillManifest? best = null;
        var bestLen = 0;
        foreach (var skill in Skills.Where(s => s.Enabled))
        {
            foreach (var trigger in skill.Triggers)
            {
                var t = trigger.Trim();
                if (t.Length == 0)
                {
                    continue;
                }

                if (text.Contains(t, StringComparison.OrdinalIgnoreCase) && t.Length > bestLen)
                {
                    best = skill;
                    bestLen = t.Length;
                }
            }
        }

        return best;
    }

    public GeneratedSkillManifest? FindById(string skillId)
    {
        var id = (skillId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            return null;
        }

        return Skills.FirstOrDefault(s =>
            string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public string CompactCatalogForPrompt()
    {
        var enabled = Skills.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return string.Empty;
        }

        var lines = enabled.Select(s =>
            $"- {s.Id}: {s.Title} ({s.Kind}) — triggers: {string.Join(", ", s.Triggers.Take(4))}");
        return "### Saved generated skills\n" + string.Join("\n", lines);
    }

    public bool TrySetEnabled(string skillId, bool enabled)
    {
        var skill = FindById(skillId);
        if (skill is null || string.IsNullOrWhiteSpace(skill.DirectoryPath))
        {
            return false;
        }

        var manifestPath = GeneratedSkillPaths.ManifestPath(skill.DirectoryPath);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    prop.WriteTo(writer);
                }

                writer.WriteBoolean("enabled", enabled);
                writer.WriteEndObject();
            }

            File.WriteAllBytes(manifestPath, stream.ToArray());
            Reload();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static GeneratedSkillManifest? TryLoadManifest(string manifestPath, string directory)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            var id = ReadString(root, "id");
            if (id.Length == 0)
            {
                id = Path.GetFileName(directory);
            }

            DateTime created = DateTime.UtcNow;
            if (root.TryGetProperty("createdAt", out var caEl))
            {
                _ = DateTime.TryParse(caEl.GetString(), out created);
            }

            return new GeneratedSkillManifest
            {
                Id = id,
                Title = ReadString(root, "title"),
                Summary = ReadString(root, "summary"),
                Category = ReadString(root, "category").Length == 0 ? "Generated" : ReadString(root, "category"),
                Version = root.TryGetProperty("version", out var vEl) && vEl.TryGetInt32(out var v) ? v : 1,
                Enabled = !root.TryGetProperty("enabled", out var enEl) || enEl.GetBoolean(),
                CreatedAtUtc = created.ToUniversalTime(),
                Triggers = ReadStringList(root, "triggers"),
                Kind = ReadString(root, "kind").Length == 0 ? "prompt" : ReadString(root, "kind"),
                ScriptFile = ReadString(root, "script"),
                OutboundPromptBlock = ReadString(root, "outboundPromptBlock"),
                TestCommand = ReadString(root, "testCommand"),
                SourceTurn = ReadString(root, "sourceTurn"),
                DirectoryPath = directory,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty).Trim() : string.Empty;

    private static List<string> ReadStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return el.EnumerateArray()
            .Select(x => (x.GetString() ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }
}
