namespace Hermes.InAppAssistant;

public sealed class AppAssistantService
{
    private readonly OpenRouterChatClient _client;
    private readonly IAppAssistantLogger _logger;

    public AppAssistantService(OpenRouterChatClient? client = null, IAppAssistantLogger? logger = null)
    {
        _client = client ?? new OpenRouterChatClient();
        _logger = logger ?? NullAppAssistantLogger.Instance;
    }

    public async Task<string> AskAsync(
        AppAssistantOptions options,
        IAppAssistantContextProvider contextProvider,
        IReadOnlyList<AssistantChatTurn> history,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("Message is empty.", nameof(userMessage));
        }

        var modelId = OpenRouterChatClient.NormalizeModelId(options.Model);
        _logger.Info(
            $"[openrouter-assistant] ask app={options.ApplicationId} model={modelId} " +
            $"key={AssistantLogRedaction.MaskApiKey(options.OpenRouterApiKey)} " +
            $"user={AssistantLogRedaction.Preview(userMessage)} history={history.Count}");

        var system = AppAssistantKnowledge.BuildSystemPrompt(
            options.ApplicationId,
            contextProvider.GetLiveContextSnapshot());

        var messages = new List<AssistantChatMessage>
        {
            new("system", system),
        };

        var tail = history
            .Where(t => !string.IsNullOrWhiteSpace(t.Content))
            .TakeLast(Math.Max(1, options.MaxHistoryTurns))
            .ToList();

        foreach (var turn in tail)
        {
            var role = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? "assistant"
                : "user";
            messages.Add(new AssistantChatMessage(role, turn.Content.Trim()));
        }

        messages.Add(new AssistantChatMessage("user", userMessage.Trim()));

        var started = Environment.TickCount64;
        try
        {
            _logger.Info(
                $"[openrouter-assistant] request POST chat/completions model={modelId} messages={messages.Count}");

            var reply = await _client.CompleteAsync(options, messages, cancellationToken)
                .ConfigureAwait(false);

            var ms = Environment.TickCount64 - started;
            _logger.Info(
                $"[openrouter-assistant] ok ms={ms} replyChars={reply.Length} " +
                $"preview={AssistantLogRedaction.Preview(reply)}");

            return reply;
        }
        catch (Exception ex)
        {
            var ms = Environment.TickCount64 - started;
            _logger.Error($"[openrouter-assistant] failed ms={ms} {ex.Message}");
            throw;
        }
    }
}
