using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Wpf.Services;

/// <summary>JSON row in Supabase <c>messages.content</c> for project + mode sync (Android / relay).</summary>
public static class HermesWpfSessionContextPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string BuildJson(string projectName, string modeId)
    {
        var dto = new SessionDto
        {
            HermesWpf = "session",
            Project = string.IsNullOrWhiteSpace(projectName) ? "(no project)" : projectName.Trim(),
            Mode = string.IsNullOrWhiteSpace(modeId) ? HermesChatModeResolver.ModeAgent : modeId.Trim(),
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static bool IsSessionPayload(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(content.Trim());
            return doc.RootElement.TryGetProperty("hermes_wpf", out var hp)
                   && string.Equals(hp.GetString(), "session", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed class SessionDto
    {
        [JsonPropertyName("hermes_wpf")]
        public string HermesWpf { get; set; } = "session";

        [JsonPropertyName("project")]
        public string Project { get; set; } = "";

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = HermesChatModeResolver.ModeAgent;
    }
}
