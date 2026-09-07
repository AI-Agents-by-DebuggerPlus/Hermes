using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Hermes.EnglishLearning.Services;

public static class AppVersion
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

/// <summary>Safe path helpers for removable drives / dead letters in settings.</summary>
public static class PathSafety
{
    public static bool IsReadyPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path!.Trim());
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root)) return false;
            var drive = new DriveInfo(root);
            return drive.IsReady;
        }
        catch
        {
            return false;
        }
    }

    public static bool DirectoryUsable(string? path)
    {
        if (!IsReadyPath(path)) return false;
        try
        {
            return Directory.Exists(path!);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Preferred folder for OpenFileDialog; never returns a dead-drive path.</summary>
    public static string? SafeInitialDirectory(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            try
            {
                var dir = c!.Trim();
                if (File.Exists(dir))
                    dir = Path.GetDirectoryName(dir) ?? string.Empty;
                if (DirectoryUsable(dir))
                    return Path.GetFullPath(dir);
            }
            catch
            {
                // next
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

    public static void SanitizeLessonPaths(AppSettings s)
    {
        if (s == null) return;
        if (!string.IsNullOrWhiteSpace(s.LessonsFolder) && !IsReadyPath(s.LessonsFolder))
        {
            s.LessonsFolder = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(s.LastLocalLessonPath))
        {
            try
            {
                if (!IsReadyPath(s.LastLocalLessonPath) || !File.Exists(s.LastLocalLessonPath))
                    s.LastLocalLessonPath = string.Empty;
            }
            catch
            {
                s.LastLocalLessonPath = string.Empty;
            }
        }
    }
}
