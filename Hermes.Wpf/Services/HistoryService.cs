using System.Text.Json;
using Hermes.Wpf.Models;
using System.IO;

namespace Hermes.Wpf.Services;

public sealed class HistoryService
{
    private readonly string _historyRoot;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public HistoryService()
    {
        _historyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf",
            "history");
        Directory.CreateDirectory(_historyRoot);
    }

    public string GetHistoryFilePath(string projectName) => BuildProjectHistoryFilePath(projectName);

    public async Task SaveAsync(SessionHistory sessionHistory)
    {
        var filePath = BuildProjectHistoryFilePath(sessionHistory.ProjectName);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, sessionHistory, _jsonOptions);
    }

    public async Task<SessionHistory> LoadAsync(string projectName)
    {
        var filePath = BuildProjectHistoryFilePath(projectName);
        if (!File.Exists(filePath))
        {
            return new SessionHistory { ProjectName = projectName };
        }

        await using var stream = File.OpenRead(filePath);
        var loaded = await JsonSerializer.DeserializeAsync<SessionHistory>(stream);
        return loaded ?? new SessionHistory { ProjectName = projectName };
    }

    private string BuildProjectHistoryFilePath(string projectName)
    {
        var safeName = string.Concat(projectName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_historyRoot, $"{safeName}.json");
    }
}
