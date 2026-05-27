namespace Hermes.Wpf.Models.Biohacker;

/// <summary>Rolling aggregate of last N days of DailyHealthLog. Trend ∈ {improving, stable, declining}.</summary>
public sealed record HealthMetricsSummary(
    double AvgSleepQuality,
    double AvgEnergyMorning,
    double AvgFocus,
    double AvgMood,
    double AvgProductivity,
    double AvgStress,
    int DaysAnalyzed,
    string Trend);
