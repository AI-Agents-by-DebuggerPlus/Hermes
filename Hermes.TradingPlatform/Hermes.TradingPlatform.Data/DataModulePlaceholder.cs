using Hermes.TradingPlatform.Core.Abstractions;

namespace Hermes.TradingPlatform.Data;

/// <summary>Phase 2+: persistence (SQLite → PostgreSQL).</summary>
public sealed class DataModulePlaceholder : IPlatformModule
{
    public string Name => "Data";
}
