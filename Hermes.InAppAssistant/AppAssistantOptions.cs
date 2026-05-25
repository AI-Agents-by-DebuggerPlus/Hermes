namespace Hermes.InAppAssistant;

public sealed class AppAssistantOptions
{
    public string ApplicationId { get; init; } = "hermes-app";
    public string OpenRouterApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = OpenRouterChatClient.DefaultModel;
    public int MaxHistoryTurns { get; init; } = 12;
}
