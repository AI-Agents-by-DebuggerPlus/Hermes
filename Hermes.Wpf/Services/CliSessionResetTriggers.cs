namespace Hermes.Wpf.Services;

/// <summary>User phrases that reset Hermes CLI <c>--resume</c> for the current project.</summary>
public static class CliSessionResetTriggers
{
    public static bool Matches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        return t.Equals("новая сессия", StringComparison.OrdinalIgnoreCase)
               || t.Equals("новая cli сессия", StringComparison.OrdinalIgnoreCase)
               || t.Equals("сброс сессии", StringComparison.OrdinalIgnoreCase)
               || t.Equals("сбросить сессию", StringComparison.OrdinalIgnoreCase)
               || t.Equals("new session", StringComparison.OrdinalIgnoreCase)
               || t.Equals("reset session", StringComparison.OrdinalIgnoreCase)
               || t.Contains("сбрось сессию hermes", StringComparison.OrdinalIgnoreCase)
               || t.Contains("начни новую сессию", StringComparison.OrdinalIgnoreCase);
    }
}
