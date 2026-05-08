namespace Hermes.Wpf.Models;

/// <summary>Tracked lemma for spaced repetition telemetry (persisted).</summary>
public sealed class EnglishTutorWordState
{
    /// <summary>canonical lower lemma</summary>
    public string Lemma { get; set; } = string.Empty;

    /// <summary>known | review | learning</summary>
    public string Tier { get; set; } = "learning";

    /// <summary>Times the student saw or was quizzed on the word.</summary>
    public int ExposureCount { get; set; }

    /// <summary>Consecutive correct recalls in-session stack (approximation).</summary>
    public int SuccessStreak { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? LastUtc { get; set; }
}
