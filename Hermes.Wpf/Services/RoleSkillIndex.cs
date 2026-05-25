using System.IO;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class RoleSkillIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly LogService _log;
    private readonly object _lock = new();
    private Dictionary<AgentRole, List<GeneratedSkillManifest>> _byRole = new();
    private Dictionary<string, RoleSkillUsageEntry> _usage = new(StringComparer.OrdinalIgnoreCase);

    public RoleSkillIndex(LogService log) => _log = log;

    private static string IndexPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesWpf",
            "role-skill-index.json");

    public void Rebuild(IReadOnlyList<GeneratedSkillManifest> allSkills)
    {
        var map = new Dictionary<AgentRole, List<GeneratedSkillManifest>>();
        foreach (var role in RoleManager.AllRoles)
        {
            map[role] = [];
        }

        foreach (var skill in allSkills.Where(s => s.Enabled))
        {
            var roles = skill.Roles.Count == 0 ? [AgentRole.Universal] : skill.Roles;
            var targets = roles.Contains(AgentRole.Universal)
                ? RoleManager.AllRoles
                : roles;

            foreach (var role in targets)
            {
                map[role].Add(skill);
            }
        }

        lock (_lock)
        {
            _byRole = map;
        }

        _log.LogInfo($"[role-skill] index rebuilt ({allSkills.Count} skill(s))");
    }

    public IReadOnlyList<GeneratedSkillManifest> GetSkillsForRole(AgentRole role, int maxItems = 10)
    {
        lock (_lock)
        {
            if (!_byRole.TryGetValue(role, out var list))
            {
                return [];
            }

            return list
                .OrderByDescending(s => GetUsageCount(s.Id, role))
                .ThenByDescending(s => s.CreatedAtUtc)
                .Take(Math.Clamp(maxItems, 1, 50))
                .ToList();
        }
    }

    public void RecordUsage(string skillId, AgentRole activeRole)
    {
        var id = (skillId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            var key = $"{activeRole}:{id}";
            if (!_usage.TryGetValue(key, out var entry))
            {
                entry = new RoleSkillUsageEntry { SkillId = id, Role = activeRole.ToString() };
                _usage[key] = entry;
            }

            entry.Count++;
            entry.LastUsedUtc = DateTime.UtcNow;
        }

        _ = SaveAsync();
    }

    private int GetUsageCount(string skillId, AgentRole role)
    {
        var key = $"{role}:{skillId}";
        return _usage.TryGetValue(key, out var e) ? e.Count : 0;
    }

    public Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(IndexPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            RoleSkillIndexFile file;
            lock (_lock)
            {
                file = new RoleSkillIndexFile { Usage = _usage.Values.ToList() };
            }

            File.WriteAllText(IndexPath, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[role-skill] save failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task LoadAsync()
    {
        try
        {
            if (!File.Exists(IndexPath))
            {
                return Task.CompletedTask;
            }

            var file = JsonSerializer.Deserialize<RoleSkillIndexFile>(File.ReadAllText(IndexPath), JsonOptions);
            if (file?.Usage is null)
            {
                return Task.CompletedTask;
            }

            lock (_lock)
            {
                _usage = file.Usage.ToDictionary(
                    e => $"{e.Role}:{e.SkillId}",
                    e => e,
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[role-skill] load failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private sealed class RoleSkillIndexFile
    {
        public List<RoleSkillUsageEntry> Usage { get; set; } = [];
    }

    private sealed class RoleSkillUsageEntry
    {
        public string SkillId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTime LastUsedUtc { get; set; }
    }
}
