namespace Hermes.WpGallery.Tool.Models;

public enum LogLevel { Info, Success, Warning, Error }

public class LogEntry
{
    public DateTime  Time    { get; init; } = DateTime.Now;
    public LogLevel  Level   { get; init; } = LogLevel.Info;
    public string    Message { get; init; } = string.Empty;

    public string TimeStr => Time.ToString("HH:mm:ss");

    public string LevelTag => Level switch
    {
        LogLevel.Success => "✓",
        LogLevel.Warning => "⚠",
        LogLevel.Error   => "✕",
        _                => "•",
    };
}
