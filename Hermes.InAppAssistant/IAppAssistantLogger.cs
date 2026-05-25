namespace Hermes.InAppAssistant;

public interface IAppAssistantLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class NullAppAssistantLogger : IAppAssistantLogger
{
    public static readonly NullAppAssistantLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}
