using System.IO;
using System.Net.Http;
using System.Text;
using Hermes.WpGallery;
using Newtonsoft.Json.Linq;

namespace Hermes.WpGallery.Tool.Services;

/// <summary>Скачивает логи с WordPress (GET /wp-json/hermes/v1/logs) в папку WordPress проекта.</summary>
public class WordPressLogSyncService
{
    private readonly SettingsService _settings;

    public WordPressLogSyncService(SettingsService settings) => _settings = settings;

    public string WordPressDirectory => ResolveWordPressDirectory();

    public async Task<(bool ok, string message, int filesWritten)> SyncFromSiteAsync(
        int days = 7,
        CancellationToken ct = default)
    {
        var s = _settings.Current;
        if (!WpGalleryEndpoints.TryNormalizeSiteUrl(s.WordPressUrl, out var site, out var urlError))
            return (false, urlError ?? "URL не задан", 0);
        if (string.IsNullOrWhiteSpace(s.SecretToken))
            return (false, "Секретный токен не задан", 0);

        var url = $"{site}/wp-json/hermes/v1/logs?days={days}";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Hermes-Token", s.SecretToken);

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return (false, "На сайте нет GET /hermes/v1/logs — обновите плагин до v1.0.3+", 0);
                return (false, $"HTTP {(int)response.StatusCode}: {body}", 0);
            }

            var obj    = JObject.Parse(body);
            var files  = obj["files"] as JArray;
            if (files == null || files.Count == 0)
                return (true, "На сервере пока нет файлов логов (отправьте логи из WPF или сделайте снимок)", 0);

            var dir = WordPressDirectory;
            Directory.CreateDirectory(dir);

            var written = 0;
            foreach (var f in files)
            {
                var name    = f["name"]?.ToString();
                var content = f["content"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(name)) continue;

                var path = Path.Combine(dir, name);
                await File.WriteAllTextAsync(path, content, Encoding.UTF8, ct).ConfigureAwait(false);
                written++;
            }

            return (true, $"Сохранено файлов: {written} → {dir}", written);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка: {ex.Message}", 0);
        }
    }

    private static string ResolveWordPressDirectory()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (dir.Name.Equals("HermesWpfGallery", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(dir.FullName, "WordPress");

            var wp = Path.Combine(dir.FullName, "WordPress");
            if (Directory.Exists(wp))
                return wp;

            dir = dir.Parent;
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WordPress");
    }
}
