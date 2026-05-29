namespace Hermes.SpotTerminal.Shared.Bridge;

public sealed class SpotPlatformCommandFile
{
    public List<SpotPlatformCommand> Pending { get; set; } = [];
}

public sealed class SpotPlatformCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string? Symbol { get; set; }
    public string? Side { get; set; }
    public string? OrderType { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Price { get; set; }
    public string? OrderId { get; set; }
    public string? SkillId { get; set; }
    public bool? Enabled { get; set; }
    public string? RequestedBy { get; set; }
}

public sealed class SpotPlatformCommandResultFile
{
    public Guid CommandId { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset CompletedUtc { get; init; } = DateTimeOffset.UtcNow;
}
