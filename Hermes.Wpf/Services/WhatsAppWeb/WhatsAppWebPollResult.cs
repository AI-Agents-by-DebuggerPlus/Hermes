using System.Text.Json.Serialization;

namespace Hermes.Wpf.Services.WhatsAppWeb;

internal sealed class WhatsAppWebPollResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<WhatsAppWebPollMessage> Messages { get; set; } = [];
}

internal sealed class WhatsAppWebPollMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("hasMessageStructure")]
    public bool HasMessageStructure { get; set; }

    [JsonPropertyName("isIncoming")]
    public bool IsIncoming { get; set; } = true;
}

internal sealed class WhatsAppWebSimpleResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("chatCount")]
    public int ChatCount { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("remaining")]
    public string Remaining { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;

    [JsonPropertyName("attempt")]
    public int Attempt { get; set; }
}
