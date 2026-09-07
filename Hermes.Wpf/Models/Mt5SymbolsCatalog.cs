namespace Hermes.Wpf.Models;

/// <summary>Broker symbol list cached from Mt5Terminal/HWT (hermes/mt5_symbols.json).</summary>
public sealed class Mt5SymbolsCatalog
{
    public DateTime FetchedAtUtc { get; init; }
    public string Source { get; init; } = "Mt5Terminal";
    public string? ChartSymbol { get; init; }
    public IReadOnlyList<string> Symbols { get; init; } = [];
    public int Count => Symbols.Count;
}
