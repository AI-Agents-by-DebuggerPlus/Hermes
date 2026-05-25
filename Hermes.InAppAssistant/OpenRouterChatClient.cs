using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.InAppAssistant;

public sealed class OpenRouterChatClient : IDisposable
{
    public const string DefaultModel = "openrouter/free";

    private const string ChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OpenRouterChatClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    public async Task<string> CompleteAsync(
        AppAssistantOptions options,
        IReadOnlyList<AssistantChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.OpenRouterApiKey))
        {
            throw new InvalidOperationException(
                "OpenRouter API key is empty. Set it in application Settings → In-app assistant.");
        }

        var modelId = NormalizeModelId(options.Model);
        var apiKey = options.OpenRouterApiKey.Trim();

        var chatMessages = messages
            .Select(m => new OpenRouterMessageDto(
                string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "assistant"
                    : string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)
                        ? "system"
                        : "user",
                m.Content))
            .ToArray();

        if (chatMessages.Length == 0)
        {
            throw new InvalidOperationException("No messages to send.");
        }

        var body = new OpenRouterChatRequest { Model = modelId, Messages = chatMessages };

        using var req = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/hermes-wpf");
        req.Headers.TryAddWithoutValidation("X-Title", "Hermes In-App Assistant");
        req.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var payload = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenRouter HTTP {(int)res.StatusCode}: {Truncate(payload, 500)}");
        }

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("choices", out var choices)
            || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenRouter returned no choices.");
        }

        var content = choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
        return string.IsNullOrEmpty(content)
            ? throw new InvalidOperationException("OpenRouter returned empty content.")
            : content;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    internal static string NormalizeModelId(string? model) =>
        string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private sealed class OpenRouterChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required OpenRouterMessageDto[] Messages { get; init; }
    }

    private sealed record OpenRouterMessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}

public sealed record AssistantChatMessage(string Role, string Content);
