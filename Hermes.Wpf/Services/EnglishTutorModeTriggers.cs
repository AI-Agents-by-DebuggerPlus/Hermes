namespace Hermes.Wpf.Services;



/// <summary>DetectRussian phrases switching English tutor persona on/off (client).</summary>

public static class EnglishTutorModeTriggers

{

    /// <remarks>Matched on trimmed user-visible message (culture-invariant).</remarks>

    public static bool MatchesEnable(string message)

    {

        if (string.IsNullOrWhiteSpace(message))

        {

            return false;

        }



        var t = message.Trim().ToLowerInvariant();

        if (MatchesDisable(message))

        {

            return false;

        }



        if (t.Contains("репетитор", StringComparison.Ordinal) && (t.Contains("английск", StringComparison.Ordinal) || t.Contains("english", StringComparison.Ordinal)))

        {

            return true;

        }



        if (t.Contains("режим", StringComparison.Ordinal) && t.Contains("репетит", StringComparison.Ordinal) && !t.Contains("выкл", StringComparison.Ordinal) && !t.Contains("останов", StringComparison.Ordinal))

        {

            return true;

        }



        var learnPhrases = new[] { "учить английск", "учим английск", "будем учить английск", "начнём английск", "начнем английск", "давай английск", "давай учить английск", };



        foreach (var p in learnPhrases)

        {

            if (t.Contains(p, StringComparison.Ordinal))

            {

                return true;

            }

        }



        return t.Contains("english tutor", StringComparison.Ordinal) || t.Contains("esl mode", StringComparison.Ordinal);

    }



    public static bool MatchesDisable(string message)

    {

        if (string.IsNullOrWhiteSpace(message))

        {

            return false;

        }



        var t = message.Trim().ToLowerInvariant();

        var off = new[]

        {

            "остановить режим репетитора",
            "останови режим репетитора",
            "остановить репетитора",
            "останови репетитора",
            "выключить режим репетитора",
            "выключи режим репетитора",
            "выключить репетитора",
            "выключи репетитора",
            "отключить режим репетитора",
            "не учим английск",
            "хватит репетитор",
            "выход из режим репетитора",
            "выход из режима репетитора",
            "конец режим репетитора",

            "закончим учить английск",

            "закончи учить английск",

            "хватит английск",

            "достаточно английск",

            "выключи репетит",

            "выключите репетит",

            "отключи репетит",

            "выйдем из режим",

            "выйди из режим",

            "режим репетитора выкл",

            "общий режим",

            "exit esl",

            "stop english tutor",

        };



        foreach (var o in off)

        {

            if (t.Contains(o, StringComparison.Ordinal))

            {

                return true;

            }

        }



        return false;

    }

}


