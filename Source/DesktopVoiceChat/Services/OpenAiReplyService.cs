using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopVoiceChat.Services;

/// <summary>Минимальный вызов Chat Completions для тестовых ответов в чате.</summary>
public sealed class OpenAiReplyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public OpenAiReplyService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    public async Task<string> GetReplyAsync(
        string apiKey,
        string model,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key пуст.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4o-mini";
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("Текст запроса пуст.", nameof(userMessage));
        }

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        var body = new ChatCompletionRequest
        {
            Model = model.Trim(),
            Messages =
            [
                new ChatMessageDto("system", "Ты в тестовом чате. Отвечай кратко по-русски, по существу."),
                new ChatMessageDto("user", userMessage.Trim()),
            ],
        };

        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var payload = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI HTTP {(int)res.StatusCode}: {TruncateForLog(payload, 500)}");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var choice0 = root.GetProperty("choices")[0];
        var message = choice0.GetProperty("message");
        var content = message.GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(content)
            ? throw new InvalidOperationException("Ответ OpenAI без текста.")
            : content.Trim();
    }

    private static string TruncateForLog(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";

    private sealed record ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required ChatMessageDto[] Messages { get; init; }
    }

    private sealed record ChatMessageDto(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
