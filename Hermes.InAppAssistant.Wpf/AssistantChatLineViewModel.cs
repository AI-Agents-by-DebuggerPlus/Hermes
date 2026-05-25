namespace Hermes.InAppAssistant.Wpf;

public sealed class AssistantChatLineViewModel
{
    public AssistantChatLineViewModel(bool isUser, string text, DateTimeOffset at)
    {
        IsUser = isUser;
        Text = text;
        At = at;
    }

    public bool IsUser { get; }
    public string Text { get; }
    public DateTimeOffset At { get; }
    public string Speaker => IsUser ? "You" : "Assistant";
}
