using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Wpf.Models;

/// <summary>One row in hermes/corrections.jsonl (self-learning design §2).</summary>
public sealed class CorrectionLogEntry
{
    [JsonPropertyName("ts")]
    public string Ts { get; init; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("session")]
    public string Session { get; init; } = string.Empty;

    [JsonPropertyName("project")]
    public string Project { get; init; } = string.Empty;

    [JsonPropertyName("task_type")]
    public string TaskType { get; init; } = string.Empty;

    [JsonPropertyName("what")]
    public string What { get; init; } = string.Empty;

    [JsonPropertyName("actual_first")]
    public string ActualFirst { get; init; } = string.Empty;

    [JsonPropertyName("corrected_to")]
    public string CorrectedTo { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "implicit";

    [JsonPropertyName("user_text")]
    public string UserText { get; init; } = string.Empty;
}
