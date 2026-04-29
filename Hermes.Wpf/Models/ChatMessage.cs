namespace Hermes.Wpf.Models;

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Text { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
