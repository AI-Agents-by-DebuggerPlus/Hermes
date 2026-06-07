using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hermes.Wpf.Services.WhatsAppWeb;

/// <summary>Persists WhatsApp message ids already handled so restarts do not re-inject DOM history into Hermes chat.</summary>
internal sealed class WhatsAppSeenMessageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private bool _dirty;

    public WhatsAppSeenMessageStore(string contactDisplayName)
    {
        var safeContact = SanitizeFileToken(contactDisplayName);
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, $"whatsapp_seen_{safeContact}.json");
        Load();
    }

    public int Count => _ids.Count;

    public IReadOnlyCollection<string> AllIds => _ids;

    public bool Contains(string id) => _ids.Contains(id);

    public bool Add(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!_ids.Add(id.Trim()))
        {
            return false;
        }

        _dirty = true;
        return true;
    }

    public void AddRange(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            Add(id);
        }
    }

    public async Task FlushAsync()
    {
        if (!_dirty)
        {
            return;
        }

        var payload = new WhatsAppSeenFileDto
        {
            UpdatedAt = DateTimeOffset.Now,
            MessageIds = _ids.OrderBy(x => x, StringComparer.Ordinal).ToList(),
        };

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, payload, JsonOptions).ConfigureAwait(false);
        _dirty = false;
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<WhatsAppSeenFileDto>(json);
            if (dto?.MessageIds is null)
            {
                return;
            }

            foreach (var id in dto.MessageIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _ids.Add(id.Trim());
                }
            }
        }
        catch
        {
            // corrupt file — start fresh
        }
    }

    private static string SanitizeFileToken(string contactDisplayName)
    {
        var name = string.IsNullOrWhiteSpace(contactDisplayName) ? "My Fido" : contactDisplayName.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return Convert.ToHexString(hash)[..16];
    }

    private sealed class WhatsAppSeenFileDto
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public List<string> MessageIds { get; set; } = [];
    }
}
