using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishTutorClient.Models;

public sealed class TutorExercise
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<string> Words { get; set; } = new();
    public string? ExpectedAnswer { get; set; }
    public string WordsLang { get; set; } = "en";
    public string QuestionLang { get; set; } = "ru";
}

public sealed class TutorSessionDocument
{
    public string Title { get; set; } = "English level test";
    public List<TutorExercise> Exercises { get; set; } = new();
}

public sealed class TutorFeedback
{
    public bool IsCorrect { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum ChatBubbleKind
{
    Tutor,
    User,
    System,
}

public sealed class ChatBubble
{
    public ChatBubbleKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Question { get; set; }
    public string? Words { get; set; }
}

/// <summary>Wire format from Hermes → EnglishTutorClient.</summary>
public sealed class TutorWireMessage
{
    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("text")]
    public string? Text { get; set; }

    [JsonProperty("question")]
    public string? Question { get; set; }

    [JsonProperty("words")]
    public List<string>? Words { get; set; }

    [JsonProperty("questionLang")]
    public string? QuestionLang { get; set; }

    [JsonProperty("wordsLang")]
    public string? WordsLang { get; set; }

    [JsonProperty("speak")]
    public bool? Speak { get; set; }

    [JsonProperty("feedback")]
    public string? Feedback { get; set; }

    [JsonProperty("correct")]
    public bool? Correct { get; set; }

    public static bool TryParse(string content, out TutorWireMessage? msg)
    {
        msg = null;
        var t = (content ?? string.Empty).Trim();
        if (t.Length == 0 || t[0] != '{') return false;
        try
        {
            var obj = JObject.Parse(t);
            var type = obj["type"]?.ToString() ?? string.Empty;
            if (!type.StartsWith("tutor", System.StringComparison.OrdinalIgnoreCase)
                && type != "english_tutor"
                && !obj.ContainsKey("question")
                && !obj.ContainsKey("words"))
            {
                // bilingual TTS JSON from Android path — ignore as exercise
                if (obj.ContainsKey("ru") || obj.ContainsKey("en"))
                    return false;
            }

            msg = obj.ToObject<TutorWireMessage>();
            return msg != null;
        }
        catch
        {
            return false;
        }
    }
}
