namespace Hermes.Wpf.Models;

public sealed class SessionHistory
{
    public required string ProjectName { get; init; }
    public List<ChatMessage> Messages { get; init; } = [];
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
}
