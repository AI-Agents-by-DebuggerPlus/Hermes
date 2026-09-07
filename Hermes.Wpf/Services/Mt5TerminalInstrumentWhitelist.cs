using System.IO;
using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>Symbols Trading Analytics may route to Mt5Terminal/HWT.</summary>
public static class Mt5TerminalInstrumentWhitelist
{
    private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        "XAUUSD", "XAUUSDm", "GOLD",
        "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD",
        "EURJPY", "GBPJPY", "EURGBP",
        "BTCUSD", "BTCUSDT", "ETHUSD",
        "US500", "US30", "NAS100", "SPX500", "USTEC",
        "WTI", "BRENT", "OIL",
    };

    private static readonly object Gate = new();
    private static HashSet<string> _user = new(StringComparer.OrdinalIgnoreCase);
    private static bool _userLoaded;

    private static string UserFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf",
            "mt5_instrument_whitelist.json");

    public static string Normalize(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        var s = symbol.Trim().ToUpperInvariant();
        s = s.Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (s.EndsWith(".M", StringComparison.Ordinal))
        {
            s = s[..^2] + "m";
        }

        return s;
    }

    public static bool IsAllowed(string? symbol)
    {
        EnsureUserLoaded();
        return IsAllowedCore(symbol);
    }

    public static bool TryAddUserSymbol(string? symbol, out string normalized)
    {
        normalized = Normalize(symbol);
        if (normalized.Length == 0)
        {
            return false;
        }

        EnsureUserLoaded();
        lock (Gate)
        {
            if (IsAllowedCore(normalized))
            {
                return true;
            }

            _user.Add(normalized);
            SaveUserSymbolsLocked();
        }

        return true;
    }

    private static bool IsAllowedCore(string? symbol)
    {
        var n = Normalize(symbol);
        if (n.Length == 0)
        {
            return false;
        }

        if (BuiltIn.Contains(n) || _user.Contains(n))
        {
            return true;
        }

        foreach (var baseSym in BuiltIn.Concat(_user))
        {
            if (n.StartsWith(baseSym, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MatchesChartSymbol(string? signalSymbol, string? chartSymbol)
    {
        var a = Normalize(signalSymbol);
        var b = Normalize(chartSymbol);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
               || b.StartsWith(a, StringComparison.OrdinalIgnoreCase)
               || a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ListForDisplay()
    {
        EnsureUserLoaded();
        return BuiltIn.Concat(_user).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void EnsureUserLoaded()
    {
        lock (Gate)
        {
            if (_userLoaded)
            {
                return;
            }

            _user = LoadUserSymbolsLocked();
            _userLoaded = true;
        }
    }

    private static HashSet<string> LoadUserSymbolsLocked()
    {
        try
        {
            if (!File.Exists(UserFilePath))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(UserFilePath);
            var dto = JsonSerializer.Deserialize<UserWhitelistFile>(json);
            if (dto?.Symbols is null || dto.Symbols.Count == 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return dto.Symbols
                .Select(Normalize)
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveUserSymbolsLocked()
    {
        var dir = Path.GetDirectoryName(UserFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var dto = new UserWhitelistFile
        {
            Symbols = _user.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(UserFilePath, json);
    }

    private sealed class UserWhitelistFile
    {
        public List<string> Symbols { get; set; } = [];
    }
}
