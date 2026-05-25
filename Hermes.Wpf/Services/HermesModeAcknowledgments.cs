namespace Hermes.Wpf.Services;

/// <summary>Short UI replies when user switches Hermes.Wpf persona modes.</summary>
public static class HermesModeAcknowledgments
{
    public const string AgentModeActivated = "Принято. Режим агента Hermes (WSL) активирован.";

    public const string AssistantModeActivated = "Принято. Режим ассистента (OpenRouter) активирован. Команда «режим агента» — обратно к Hermes.";

    public const string TradingModeActivated = "Принято. Режим трейдинга активирован.";
}
