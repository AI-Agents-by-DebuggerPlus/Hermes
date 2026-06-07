using System.IO;
using System.Security.Cryptography;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class RoleExperienceCapture
{
    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;
    private readonly MemoryExtractorService _extractor = new();
    private readonly object _dedupLock = new();
    private readonly LinkedList<string> _recentHashes = new();

    public RoleExperienceCapture(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public Task<bool> TryCaptureAsync(MemoryDraft draft, AgentRole activeRole, string vaultPath) =>
        TryCaptureAsync(draft, activeRole, vaultPath, null);

    public Task<bool> CaptureIfNeededAsync(
        MemoryDraft draft,
        AgentRole activeRole,
        string vaultPath,
        LocalCaptureOptions? options = null) =>
        TryCaptureAsync(draft, activeRole, vaultPath, options);

    public Task<bool> TryCaptureAsync(
        MemoryDraft draft,
        AgentRole activeRole,
        string vaultPath,
        LocalCaptureOptions? options)
    {
        if (string.IsNullOrWhiteSpace(vaultPath) || !Directory.Exists(vaultPath))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(TryCaptureCore(draft, activeRole, vaultPath, options));
    }

    private bool TryCaptureCore(MemoryDraft draft, AgentRole activeRole, string vaultPath, LocalCaptureOptions? options)
    {
        var settings = _settings();
        var force = options?.ForceRoleCaptureWhenDisabled == true;
        if (!force && !settings.RoleAutoCapture)
        {
            return false;
        }

        if (!force && activeRole == AgentRole.Universal)
        {
            return false;
        }

        var minImportance = options?.MinImportanceOverride ?? settings.RoleAutoCaptureMinImportance;
        var contentLen = (draft.Problem?.Length ?? 0) + (draft.Solution?.Length ?? 0);
        var minLen = options?.BypassMinLength == true ? 24 : settings.RoleAutoCaptureMinLength;

        if (draft.Importance < minImportance || contentLen < minLen)
        {
            return false;
        }

        var type = (draft.Type ?? string.Empty).Trim().ToLowerInvariant();
        if (type is not ("procedural" or "semantic"))
        {
            return false;
        }

        if (!_extractor.ShouldSave(draft))
        {
            return false;
        }

        var hash = ComputeDedupHash(draft);
        lock (_dedupLock)
        {
            if (_recentHashes.Contains(hash))
            {
                return false;
            }

            _recentHashes.AddFirst(hash);
            while (_recentHashes.Count > 50)
            {
                _recentHashes.RemoveLast();
            }
        }

        draft.Tags ??= [];
        if (!draft.Tags.Contains("auto-captured", StringComparer.OrdinalIgnoreCase))
        {
            draft.Tags.Add("auto-captured");
        }

        if (options?.BypassMinLength == true
            && !draft.Tags.Contains("local-handler", StringComparer.OrdinalIgnoreCase))
        {
            draft.Tags.Add("local-handler");
        }

        var roleTag = GetRoleTag(activeRole);
        if (!draft.Tags.Contains(roleTag, StringComparer.OrdinalIgnoreCase))
        {
            draft.Tags.Add(roleTag);
        }

        var folder = Path.Combine(vaultPath, RoleKnowledgeSubfolder(activeRole));
        Directory.CreateDirectory(folder);
        var stamp = DateTime.UtcNow;
        var fileName = $"{stamp:yyyy-MM-dd_HH-mm-ss}_{type}.md";
        var path = Path.Combine(folder, fileName);
        var body = _extractor.GenerateMarkdown(draft);
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var title = (draft.Problem ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "memory";
        _log.LogInfo($"[role-capture] Auto-saved {type} memory for role {activeRole}: {title}");
        return true;
    }

    private static string GetRoleTag(AgentRole role) =>
        role switch
        {
            AgentRole.Trader => "trading",
            AgentRole.Developer => "development",
            AgentRole.EnglishTutor => "english",
            AgentRole.PersonalManager => "productivity",
            AgentRole.UtilitiesManager => "utilities",
            AgentRole.Biohacker => "health",
            _ => "universal",
        };

    private static string RoleKnowledgeSubfolder(AgentRole role) =>
        role switch
        {
            AgentRole.Trader => Path.Combine("Knowledge", "Trading"),
            AgentRole.Developer => Path.Combine("Knowledge", "Development"),
            AgentRole.EnglishTutor => Path.Combine("Knowledge", "English"),
            AgentRole.PersonalManager => Path.Combine("Knowledge", "Productivity"),
            AgentRole.UtilitiesManager => Path.Combine("Knowledge", "Utilities"),
            AgentRole.Biohacker => Path.Combine("Health", "Journal"),
            _ => Path.Combine("Knowledge", "General"),
        };

    private static string ComputeDedupHash(MemoryDraft draft)
    {
        var sample = (draft.Problem ?? string.Empty) + (draft.Solution ?? string.Empty);
        if (sample.Length > 200)
        {
            sample = sample[..200];
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sample));
        return Convert.ToHexString(bytes);
    }
}
