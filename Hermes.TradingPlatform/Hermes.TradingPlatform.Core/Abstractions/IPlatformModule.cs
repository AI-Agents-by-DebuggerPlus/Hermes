namespace Hermes.TradingPlatform.Core.Abstractions;

/// <summary>Marker for future modular backend services (Phase 2+).</summary>
public interface IPlatformModule
{
    string Name { get; }
}
