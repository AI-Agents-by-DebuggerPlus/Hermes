using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Hermes.RemoteTerminal.Xp;

internal static class AppVersion
{
    public static string Display
    {
        get
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v == null) return "0.0.0";
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch
            {
                return "0.0.0";
            }
        }
    }
}

/// <summary>File log next to EXE (logs/) + in-memory session for LogForm.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly List<string> Session = new List<string>();
    private const int MaxSessionLines = 4000;
    private static StreamWriter _writer;
    private static string _path;

    public static event Action<string> LineAdded;

    public static string LogDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    public static string LogPath =>
        string.IsNullOrEmpty(_path)
            ? Path.Combine(LogDir, "remote_terminal_xp_" + DateTime.Now.ToString("yyyyMMdd") + ".log")
            : _path;

    public static void StartSession()
    {
        lock (Gate)
        {
            try { if (_writer != null) _writer.Dispose(); } catch { /* ignore */ }
            _writer = null;
            Session.Clear();
            try { Directory.CreateDirectory(LogDir); } catch { /* ignore */ }

            _path = Path.Combine(LogDir, "remote_terminal_xp_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            try
            {
                var fs = new FileStream(
                    _path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
            }
            catch
            {
                _writer = null;
            }

            var header = "=== session " + DateTime.Now.ToString("o")
                         + " v" + AppVersion.Display + " ===";
            Session.Add(header);
            TryWriteLine(header);
        }

        Info("Log session started");
        Info("App version: " + AppVersion.Display);
        Info("Running on: " + SystemInfo.Describe());
        Info("Log file: " + LogPath);
    }

    public static IList<string> GetSessionLines()
    {
        lock (Gate)
        {
            return new List<string>(Session);
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + (msg ?? string.Empty);
        lock (Gate)
        {
            Session.Add(line);
            while (Session.Count > MaxSessionLines)
                Session.RemoveAt(0);
            TryWriteLine(line);
        }

        var h = LineAdded;
        if (h != null) h(line);
    }

    private static void TryWriteLine(string line)
    {
        try
        {
            if (_writer != null) _writer.WriteLine(line);
        }
        catch
        {
            // ignore
        }
    }
}
