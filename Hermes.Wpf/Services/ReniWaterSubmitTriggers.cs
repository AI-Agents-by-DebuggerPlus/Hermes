namespace Hermes.Wpf.Services;

public static class ReniWaterSubmitTriggers
{
    private static readonly string[] SubmitVerbs =
    [
        "передай",
        "передать",
        "отправ",
        "запуст",
        "сдай",
        "сдать",
        "submit",
    ];

    /// <summary>Phrases that always run Reni vodokanal submit (no Hermes CLI).</summary>
    private static readonly string[] DirectSubmitPhrases =
    [
        "передай показания",
        "передать показания",
        "передай показан",
        "передать показан",
        "показания воды",
        "показания водоканал",
        "показаний воды",
        "показания рени",
        "водоканал показания",
    ];

    public static bool MatchesSubmit(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (ReniWaterScheduleParser.IsSchedulePhrase(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();

        if (t.Contains("run_submit", StringComparison.Ordinal) || t.Contains("reni_water", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var phrase in DirectSubmitPhrases)
        {
            if (t.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (MatchesOtherUtilityOnly(t))
        {
            return false;
        }

        var hasReniContext = t.Contains("водоканал", StringComparison.Ordinal)
                             || t.Contains("reni", StringComparison.Ordinal)
                             || t.Contains("рени", StringComparison.Ordinal)
                             || t.Contains("вод", StringComparison.Ordinal)
                             || t.Contains("показан", StringComparison.Ordinal);

        if (!hasReniContext)
        {
            return false;
        }

        foreach (var v in SubmitVerbs)
        {
            if (t.Contains(v, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>User asked about electricity/gas only — do not hijack to Reni water.</summary>
    private static bool MatchesOtherUtilityOnly(string t) =>
        (t.Contains("электр", StringComparison.Ordinal) || t.Contains("газ", StringComparison.Ordinal))
        && !t.Contains("вод", StringComparison.Ordinal)
        && !t.Contains("водоканал", StringComparison.Ordinal)
        && !t.Contains("reni", StringComparison.Ordinal);

    public static bool MatchesLogin(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        return (t.Contains("водоканал", StringComparison.Ordinal)
                || t.Contains("reni_water", StringComparison.Ordinal)
                || t.Contains("рени", StringComparison.Ordinal))
               && (t.Contains("login", StringComparison.Ordinal)
                   || t.Contains("вход", StringComparison.Ordinal)
                   || t.Contains("логин", StringComparison.Ordinal));
    }
}
