using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hermes.EnglishLearning.Xp;

/// <summary>File log next to EXE (logs/) + in-memory session for UI window.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly List<string> Session = new List<string>();
    private const int MaxSessionLines = 4000;

    public static event Action<string> LineAdded;

    public static string LogDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    public static string LogPath =>
        Path.Combine(LogDir, "english_learning_xp_" + DateTime.Now.ToString("yyyyMMdd") + ".log");

    public static void StartSession()
    {
        lock (Gate)
        {
            try { Directory.CreateDirectory(LogDir); } catch { /* ignore */ }
            var header = "=== session " + DateTime.Now.ToString("o") + " ===";
            Session.Clear();
            Session.Add(header);
            try
            {
                File.AppendAllText(LogPath, header + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* ignore */ }
        }

        Info("Log session started");
        Info("Log directory: " + LogDir);
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
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDir);
                Session.Add(line);
                while (Session.Count > MaxSessionLines)
                    Session.RemoveAt(0);
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // ignore disk errors
        }

        var h = LineAdded;
        if (h != null)
        {
            try { h(line); } catch { /* ignore UI */ }
        }
    }
}
