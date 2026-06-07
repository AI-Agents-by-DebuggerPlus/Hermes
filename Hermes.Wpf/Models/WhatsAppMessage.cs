namespace Hermes.Wpf.Models;

public sealed class WhatsAppMessage
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required string FromName { get; init; }
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.Now;
}
