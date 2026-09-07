using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Hermes.EnglishLearning.Xp;

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

internal static class PathSafety
{
    public static bool IsReadyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root)) return false;
            return new DriveInfo(root).IsReady;
        }
        catch
        {
            return false;
        }
    }

    public static bool DirectoryUsable(string path)
    {
        if (!IsReadyPath(path)) return false;
        try { return Directory.Exists(path); }
        catch { return false; }
    }

    public static string SafeInitialDirectory(params string[] candidates)
    {
        if (candidates != null)
        {
            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                try
                {
                    var dir = c.Trim();
                    if (File.Exists(dir))
                        dir = Path.GetDirectoryName(dir) ?? string.Empty;
                    if (DirectoryUsable(dir))
                        return Path.GetFullPath(dir);
                }
                catch { /* next */ }
            }
        }

        try
        {
            var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lessons");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
        catch
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}

/// <summary>File log next to EXE (logs/) + in-memory session for UI window.</summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly List<string> Session = new List<string>();
    private const int MaxSessionLines = 4000;
    private static StreamWriter _writer;
    private static string _path;

    public static event Action<string> LineAdded;

    public static string LogDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    public static string LogPath =>
        string.IsNullOrEmpty(_path)
            ? Path.Combine(LogDir, "english_learning_xp_" + DateTime.Now.ToString("yyyyMMdd") + ".log")
            : _path;

    public static void StartSession()
    {
        lock (Gate)
        {
            try { if (_writer != null) _writer.Dispose(); } catch { /* ignore */ }
            _writer = null;
            Session.Clear();
            try { Directory.CreateDirectory(LogDir); } catch { /* ignore */ }

            _path = Path.Combine(LogDir, "english_learning_xp_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
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
                         + " pid=" + GetPid() + " v" + AppVersion.Display + " ===";
            Session.Add(header);
            TryWriteLine(header);
        }

        Info("Log session started");
        Info("App version: " + AppVersion.Display);
        Info("Running on: " + SystemInfo.Describe());
        Info("Log directory: " + LogDir);
        Info("Log file: " + LogPath);
        PurgeOldLogs(3);
    }

    /// <summary>Delete english_learning_xp_*.log older than keepDays.</summary>
    public static void PurgeOldLogs(int keepDays)
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var cutoff = DateTime.Now.Date.AddDays(-Math.Max(1, keepDays));
            foreach (var file in Directory.GetFiles(LogDir, "english_learning_xp_*.log"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
                    var parts = name.Split('_');
                    var day = parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
                    // Skip clock-skew XP dates like 20080808 by also checking file LastWriteTimeUtc.
                    DateTime dated = DateTime.MinValue;
                    var parsed = day.Length == 8
                                 && DateTime.TryParseExact(day, "yyyyMMdd", null,
                                     System.Globalization.DateTimeStyles.None, out dated);
                    var stamp = parsed ? dated.Date : File.GetLastWriteTime(file).Date;
                    // Far-future/past clock: use LastWriteTime if year is absurd.
                    if (parsed && (dated.Year < 2020 || dated.Year > DateTime.Now.Year + 1))
                        stamp = File.GetLastWriteTime(file).Date;
                    if (stamp < cutoff)
                    {
                        File.Delete(file);
                        Info("Purged old log: " + Path.GetFileName(file));
                    }
                }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Warn("Log purge: " + ex.Message);
        }
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
        if (h != null)
        {
            try { h(line); } catch { /* ignore UI */ }
        }
    }

    private static void TryWriteLine(string line)
    {
        try
        {
            if (_writer != null)
            {
                _writer.WriteLine(line);
                _writer.Flush();
                return;
            }

            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // ignore disk errors
        }
    }

    private static int GetPid()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
        catch { return 0; }
    }
}
