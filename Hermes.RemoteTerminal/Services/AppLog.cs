using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Hermes.RemoteTerminal.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly ConcurrentQueue<string> Session = new();
    private static StreamWriter? _writer;
    private static string? _path;
    private const int MaxSession = 4000;

    public static event Action<string>? LineAdded;

    public static string LogDir => Path.Combine(AppContext.BaseDirectory, "logs");

    public static string LogPath =>
        _path ?? Path.Combine(LogDir, $"remote_terminal_{DateTime.Now:yyyyMMdd}.log");

    public static void StartSession()
    {
        lock (Gate)
        {
            try { _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;
            while (Session.TryDequeue(out _)) { }

            Directory.CreateDirectory(LogDir);
            _path = Path.Combine(LogDir, $"remote_terminal_{DateTime.Now:yyyyMMdd}.log");
            try
            {
                var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
                {
                    AutoFlush = true,
                };
            }
            catch
            {
                _writer = null;
            }
        }

        Info("RemoteTerminal session start " + AppVersion.LogStamp);
        Info("Log: " + LogPath);
    }

    public static IReadOnlyList<string> GetSessionLines() => Session.ToArray();

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
        Session.Enqueue(line);
        while (Session.Count > MaxSession && Session.TryDequeue(out _)) { }

        lock (Gate)
        {
            try { _writer?.WriteLine(line); } catch { /* ignore */ }
        }

        LineAdded?.Invoke(line);
    }
}
