using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>JSON store for ProjectManager portfolio initiatives (agent CRUD + Dashboard mirror).</summary>
public sealed class PortfolioStoreService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private readonly LogService _log;
    private readonly string _storePath;
    private List<PortfolioInitiative> _items = [];

    public PortfolioStoreService(LogService log)
    {
        _log = log;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf",
            "portfolio");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, "initiatives.json");
        Load();
        if (_items.Count == 0)
        {
            SeedDemo();
        }
    }

    public event Action? Changed;

    public string StorePath => _storePath;

    public IReadOnlyList<PortfolioInitiative> GetAll()
    {
        lock (_gate)
        {
            return _items.OrderByDescending(i => i.UpdatedAtLocal).ToList();
        }
    }

    public PortfolioInitiative Add(string title, string notes, PortfolioCategory category, string? linkedWorkspace)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title required");
        }

        var item = new PortfolioInitiative
        {
            Title = title.Trim(),
            Notes = notes?.Trim() ?? string.Empty,
            Category = category,
            LinkedWorkspace = string.IsNullOrWhiteSpace(linkedWorkspace) ? null : linkedWorkspace.Trim(),
            UpdatedAtLocal = DateTime.Now,
        };

        lock (_gate)
        {
            _items.Add(item);
            SaveUnlocked();
        }

        _log.LogInfo($"[portfolio] add id={item.Id} «{item.Title}» cat={item.Category}");
        Changed?.Invoke();
        return item;
    }

    public bool TrySetCategory(string id, PortfolioCategory category, out PortfolioInitiative? item)
    {
        item = null;
        lock (_gate)
        {
            var t = _items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (t is null)
            {
                return false;
            }

            t.Category = category;
            t.UpdatedAtLocal = DateTime.Now;
            item = t;
            SaveUnlocked();
        }

        Changed?.Invoke();
        return true;
    }

    public bool TryRemove(string id)
    {
        lock (_gate)
        {
            var n = _items.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (n == 0)
            {
                return false;
            }

            SaveUnlocked();
        }

        Changed?.Invoke();
        return true;
    }

    private void SeedDemo()
    {
        Add("Claude Density Screener", "Идея скринера плотности — см. Docs/ClaudeDensityScreener", PortfolioCategory.Idea, null);
        Add("Hermes Task Scheduler", "Напоминалки агентам в Hermes.Wpf", PortfolioCategory.Current, "Utilities");
        Add("BioStack GDrive skills", "Skills для Google Drive", PortfolioCategory.InDevelopment, "BioStack");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                _items = [];
                return;
            }

            _items = JsonSerializer.Deserialize<List<PortfolioInitiative>>(File.ReadAllText(_storePath), JsonOpts) ?? [];
            _log.LogInfo($"[portfolio] loaded {_items.Count} from {_storePath}");
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[portfolio] load failed: {ex.Message}");
            _items = [];
        }
    }

    private void SaveUnlocked()
    {
        File.WriteAllText(_storePath, JsonSerializer.Serialize(_items, JsonOpts));
    }
}
