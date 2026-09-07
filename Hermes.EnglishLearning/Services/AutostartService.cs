using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Per-user autostart via HKCU Run, pointing at the current EXE path (portable / flash OK).
/// </summary>
public static class AutostartService
{
    public const string RunValueName = "HermesEnglishLearning";

    private static string RunKeyPath =>
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string CurrentExePath
    {
        get
        {
            try
            {
                var loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(loc))
                {
                    return Path.GetFullPath(loc);
                }
            }
            catch
            {
            }

            return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/')
                + Path.DirectorySeparatorChar + "Hermes.EnglishLearning.exe");
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(RunValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Autostart IsEnabled: " + ex.Message);
            return false;
        }
    }

    public static string? GetRegisteredPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(RunValueName) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return Unquote(value!.Trim());
        }
        catch
        {
            return null;
        }
    }

    public static void Enable()
    {
        var exe = CurrentExePath;
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("EXE not found: " + exe);
        }

        var quoted = "\"" + exe + "\"";
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key == null)
        {
            throw new InvalidOperationException("Cannot open HKCU Run key");
        }

        key.SetValue(RunValueName, quoted);
        AppLog.Info("Autostart enabled: " + quoted);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        AppLog.Info("Autostart disabled");
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
        {
            return s.Substring(1, s.Length - 2);
        }

        return s;
    }
}
