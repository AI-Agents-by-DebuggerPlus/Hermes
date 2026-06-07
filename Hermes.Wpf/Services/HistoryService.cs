using System.Text.Json;
using Hermes.Wpf.Models;
using System.IO;

namespace Hermes.Wpf.Services;

public sealed class HistoryService
{
    private readonly string _historyRoot;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
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
        var tempPath = filePath + ".tmp";
        await _fileGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, sessionHistory, _jsonOptions).ConfigureAwait(false);
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            File.Move(tempPath, filePath);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task<SessionHistory> LoadAsync(string projectName)
    {
        var filePath = BuildProjectHistoryFilePath(projectName);
        if (!File.Exists(filePath))
        {
            return new SessionHistory { ProjectName = projectName };
        }

        await _fileGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(filePath);
            var loaded = await JsonSerializer.DeserializeAsync<SessionHistory>(stream).ConfigureAwait(false);
            return loaded ?? new SessionHistory { ProjectName = projectName };
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private string BuildProjectHistoryFilePath(string projectName)
    {
        var safeName = string.Concat(projectName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_historyRoot, $"{safeName}.json");
    }
}
