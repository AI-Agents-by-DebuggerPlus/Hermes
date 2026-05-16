namespace Hermes.Wpf.Models;

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Text { get; init; }

    /// <summary>Local image file (e.g. Reni vodokanal screenshot).</summary>
    public string? ImagePath { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.Now;
}
