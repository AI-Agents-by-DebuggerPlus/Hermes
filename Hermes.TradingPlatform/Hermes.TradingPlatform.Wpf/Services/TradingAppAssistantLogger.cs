using Hermes.InAppAssistant;

namespace Hermes.TradingPlatform.Wpf.Services;

public sealed class TradingAppAssistantLogger : IAppAssistantLogger
{
    private readonly TradingPlatformFileLogger _log = TradingPlatformFileLogger.Instance;

    public void Info(string message) => _log.Assistant(message);

    public void Warn(string message) => _log.Warn(message);

    public void Error(string message) => _log.Error(message);
}
