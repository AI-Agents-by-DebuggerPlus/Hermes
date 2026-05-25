using Hermes.InAppAssistant;

namespace Hermes.Wpf.Services;

public sealed class WpfAppAssistantLogger : IAppAssistantLogger
{
    private readonly LogService _log;

    public WpfAppAssistantLogger(LogService log) => _log = log;

    public void Info(string message) => _log.LogInfo(message);

    public void Warn(string message) => _log.LogWarn(message);

    public void Error(string message) => _log.LogError(message);
}
