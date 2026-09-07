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

    /// <summary>Start a fresh log session for this process (append-safe for multi-instance).</summary>
    public static void BeginSession()
    {
        lock (Sync)
        {
            try { _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;
            _path = null;
            SessionLines.Clear();
            _sessionStarted = false;
        }

        EnsureSession();
        Info("Log session started");
        Info("App version: " + AppVersion.Display);
        Info("Running on: " + OsInfo.Describe());
        Info("Log directory: " + LogDirectory);
        PurgeOldLogs(keepDays: 3);
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

        try { LineAdded?.Invoke(line); }
        catch { /* ignore UI subscribers */ }
    }

    private static void EnsureSession()
    {
        lock (Sync)
        {
            if (_sessionStarted && _writer != null)
                return;

            try
            {
                Directory.CreateDirectory(LogDirectory);
                var day = DateTime.Now.ToString("yyyyMMdd");
                _path = Path.Combine(LogDirectory, "english_learning_" + day + ".log");
                // Shared read/write so a second instance does not crash on lock.
                var fs = new FileStream(
                    _path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
                var header = "=== session " + DateTime.Now.ToString("O") + " pid=" + GetPid() + " v" + AppVersion.Display + " ===";
                _writer.WriteLine(header);
                SessionLines.Add(header);
                _sessionStarted = true;
            }
            catch (Exception ex)
            {
                // Last resort: memory-only log (no crash on startup).
                _writer = null;
                _path = Path.Combine(LogDirectory, "english_learning_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                var header = "=== session (memory-only) " + DateTime.Now.ToString("O")
                             + " v" + AppVersion.Display + " openFail=" + ex.Message + " ===";
                SessionLines.Add(header);
                _sessionStarted = true;
            }
        }
    }

    private static int GetPid()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
        catch { return 0; }
    }

    /// <summary>Delete english_learning_*.log older than keepDays (keeps today's file).</summary>
    public static void PurgeOldLogs(int keepDays = 3)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            var cutoff = DateTime.Now.Date.AddDays(-Math.Max(1, keepDays));
            foreach (var file in Directory.GetFiles(LogDirectory, "english_learning_*.log"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                    // english_learning_yyyyMMdd
                    var parts = name.Split('_');
                    var day = parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
                    if (day.Length == 8
                        && DateTime.TryParseExact(day, "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var dated)
                        && dated.Date < cutoff)
                    {
                        File.Delete(file);
                        Info("Purged old log: " + Path.GetFileName(file));
                    }
                }
                catch
                {
                    // ignore single-file failures
                }
            }
        }
        catch (Exception ex)
        {
            Warn("Log purge: " + ex.Message);
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            try { _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;
            _sessionStarted = false;
        }
    }
}
