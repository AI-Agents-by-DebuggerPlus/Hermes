namespace Hermes.Wpf.Models;

/// <summary>Active Hermes agent persona for memory routing and skill resolution.</summary>
public enum AgentRole
{
    Universal,
    Developer,
    Trader,
    EnglishTutor,
    PersonalManager,
    /// <summary>Household utilities, ЖКХ, scheduled automations (Reni Water, etc.).</summary>
    UtilitiesManager,
    Biohacker,
}
