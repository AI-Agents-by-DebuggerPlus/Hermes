using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>Persists Hermes CLI session ids per WPF project for <c>hermes chat --resume</c>.</summary>
public sealed class HermesCliSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _sync = new();
    private readonly string _filePath;
    private ConcurrentDictionary<string, string> _byProject = new(StringComparer.OrdinalIgnoreCase);

    public HermesCliSessionStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesWpf");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "cli_sessions.json");
        Load();
    }

    public string? GetSessionId(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return null;
        }

        return _byProject.TryGetValue(projectName.Trim(), out var id) ? id : null;
    }

    public void SetSessionId(string projectName, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _byProject[projectName.Trim()] = sessionId.Trim();
        Save();
    }

    public void ClearSessionId(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return;
        }

        if (_byProject.TryRemove(projectName.Trim(), out _))
        {
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _byProject = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            _byProject = new ConcurrentDictionary<string, string>(
                loaded ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _byProject = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        lock (_sync)
        {
            var snapshot = _byProject.ToDictionary(static kv => kv.Key, static kv => kv.Value);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
    }
}
