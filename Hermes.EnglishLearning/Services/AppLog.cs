using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hermes.EnglishLearning.Services;

public enum LogLevel
{
    Info,
    Warn,
    Error,
}

public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly List<string> SessionLines = new();
    private static StreamWriter? _writer;
    private static string? _path;
    private static bool _sessionStarted;

    public static event Action<string>? LineAdded;

    public static string LogDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    public static string CurrentLogPath
    {
        get
        {
            EnsureSession();
            return _path ?? string.Empty;
        }
    }

    /// <summary>Start a fresh log file for this process (clears previous content).</summary>
    public static void BeginSession()
    {
        lock (Sync)
        {
            try
            {
                _writer?.Dispose();
            }
            catch
            {
            }

            _writer = null;
            _path = null;
            SessionLines.Clear();
            _sessionStarted = false;
        }

        EnsureSession();
        Info("Log session started (file cleared on restart)");
    }

    public static IReadOnlyList<string> GetSessionLines()
    {
        lock (Sync)
        {
            return SessionLines.ToArray();
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(string message, Exception ex) =>
        Write(LogLevel.Error, message + " — " + ex.Message);

    private static void Write(LogLevel level, string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level.ToString().ToUpperInvariant() + "] " + message;
        try
        {
            EnsureSession();
            lock (Sync)
            {
                SessionLines.Add(line);
                _writer?.WriteLine(line);
                _writer?.Flush();
            }
        }
        catch
        {
            // ignore disk errors
        }

        try
        {
            LineAdded?.Invoke(line);
        }
        catch
        {
            // ignore UI subscribers
        }
    }

    private static void EnsureSession()
    {
        lock (Sync)
        {
            if (_sessionStarted && _writer != null)
            {
                return;
            }

            Directory.CreateDirectory(LogDirectory);
            _path = Path.Combine(LogDirectory, "english_learning_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            // Overwrite on each app start so file matches the session window.
            _writer = new StreamWriter(_path, append: false, Encoding.UTF8) { AutoFlush = true };
            var header = "=== session " + DateTime.Now.ToString("O") + " ===";
            _writer.WriteLine(header);
            SessionLines.Add(header);
            _sessionStarted = true;
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            try
            {
                _writer?.Dispose();
            }
            catch
            {
            }

            _writer = null;
            _sessionStarted = false;
        }
    }
}
