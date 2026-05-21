namespace Hermes.Wpf.Models;

/// <summary>Ranked generated skill candidate for the current user task.</summary>
public sealed record SkillTaskMatch(
    GeneratedSkillManifest Skill,
    double Score,
    string Reason);
