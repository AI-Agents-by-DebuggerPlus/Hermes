using System.Net.Http;

namespace Hermes.WpGallery.Tool.Services;

public class SimpleHttpClientFactory
{
    private readonly HttpClient _client;

    public SimpleHttpClientFactory()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Add("User-Agent", "Hermes.WpGallery.Tool/1.0");
    }

    public HttpClient CreateClient() => _client;
}
