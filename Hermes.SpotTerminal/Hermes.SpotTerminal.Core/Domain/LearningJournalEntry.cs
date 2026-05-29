namespace Hermes.SpotTerminal.Core.Domain;

public sealed class LearningJournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset TimestampUtc { get; set; }
    public string Category { get; set; } = "Insight";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public string? RelatedSkillId { get; set; }
    public string? Symbol { get; set; }
}
