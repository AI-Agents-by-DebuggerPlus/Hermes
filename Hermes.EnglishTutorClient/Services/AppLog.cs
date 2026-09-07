using System;
using System.IO;
using System.Reflection;

namespace Hermes.EnglishTutorClient.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static string _folder = string.Empty;
    public static event Action<string>? LineWritten;
    public static event Action? Cleared;

    public static string LogFolder
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_folder))
                _folder = ResolveDefaultLogFolder();

            Directory.CreateDirectory(_folder);
            return _folder;
        }
        set
        {
            _folder = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            try { Directory.CreateDirectory(LogFolder); } catch { /* ignore */ }
        }
    }

    public static string LogPath => Path.Combine(LogFolder, "app.log");

    /// <summary>
    /// Hermes.EnglishTutorClient/logs — рядом с проектом (не LocalAppData).
    /// </summary>
    public static string ResolveDefaultLogFolder()
    {
        var root = FindProjectRoot();
        return Path.Combine(root, "logs");
    }

    private static string FindProjectRoot()
    {
        try
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Hermes.EnglishTutorClient.csproj")))
                    return dir.FullName;
                if (string.Equals(dir.Name, "Hermes.EnglishTutorClient", StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;
            }
        }
        catch { /* ignore */ }

        try
        {
            var asm = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(asm))
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(asm)!);
                for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Hermes.EnglishTutorClient.csproj")))
                        return dir.FullName;
                }
            }
        }
        catch { /* ignore */ }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                File.WriteAllText(LogPath, string.Empty);
            }
            catch { /* ignore */ }
        }

        try { Cleared?.Invoke(); } catch { /* ignore */ }
        Write("INFO", "--- log cleared (app start) ---");
        Write("INFO", "LogPath=" + LogPath);
    }

    private static void Write(string level, string msg)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { /* ignore */ }
        }

        try { LineWritten?.Invoke(line); } catch { /* ignore */ }
    }
}
