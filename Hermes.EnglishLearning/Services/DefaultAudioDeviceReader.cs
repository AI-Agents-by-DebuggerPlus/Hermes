using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hermes.EnglishLearning.Services;

/// <summary>Detects the active headset name from the current audio endpoint.</summary>
public static class DefaultAudioDeviceReader
{
    private static readonly Regex InsideParens = new(@"\(([^)]+)\)\s*$", RegexOptions.Compiled);

    public static Task<string?> TryGetDefaultRenderFriendlyNameAsync() =>
        Task.Run(TryGetDefaultRenderFriendlyName);

    public static string? TryGetDefaultRenderFriendlyName()
    {
        string? scriptPath = null;
        try
        {
            scriptPath = Path.Combine(Path.GetTempPath(), "hermes_audio_ep_" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(scriptPath, @"
$ErrorActionPreference = 'SilentlyContinue'
$eps = @(Get-PnpDevice -PresentOnly | Where-Object {
  $_.Class -eq 'AudioEndpoint' -and $_.Status -eq 'OK' -and (
    $_.FriendlyName -like 'Headphones (*' -or $_.FriendlyName -like 'Headset (*'
  )
})
$stereo = $eps | Where-Object { $_.FriendlyName -like '*Stereo*' } | Select-Object -First 1
if ($null -ne $stereo) { Write-Output $stereo.FriendlyName; exit 0 }
$any = $eps | Select-Object -First 1
if ($null -ne $any) { Write-Output $any.FriendlyName; exit 0 }
Write-Output ''
", Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return null;
            }

            var output = (proc.StandardOutput.ReadToEnd() ?? string.Empty).Trim();
            proc.WaitForExit(15000);
            if (string.IsNullOrWhiteSpace(output))
            {
                AppLog.Info("Active audio endpoint: (none found)");
                return null;
            }

            var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .LastOrDefault(s => s.Length > 0);
            AppLog.Info("Active audio endpoint: " + line);
            return line;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Active audio endpoint failed: " + ex.Message);
            return null;
        }
        finally
        {
            if (scriptPath != null)
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
    }

    public static string? MatchActiveHeadsetName(string? defaultEndpointName, System.Collections.Generic.IReadOnlyList<HeadsetBatteryInfo> headsets)
    {
        if (string.IsNullOrWhiteSpace(defaultEndpointName) || headsets == null || headsets.Count == 0)
        {
            return null;
        }

        var endpoint = defaultEndpointName!;
        var inner = endpoint;
        var m = InsideParens.Match(endpoint);
        if (m.Success)
        {
            inner = m.Groups[1].Value;
        }

        inner = inner.Trim().TrimStart('#', '\'', ' ');
        // "Grind Stereo" / "DebuggerPlus' Pixel Buds Pro Stereo" / "… Hands-Free AG Audio"
        inner = Regex.Replace(inner, @"\s*(Stereo|Hands-Free.*)$", string.Empty, RegexOptions.IgnoreCase).Trim();

        foreach (var h in headsets)
        {
            if (endpoint.IndexOf(h.Name, StringComparison.OrdinalIgnoreCase) >= 0
                || inner.IndexOf(h.Name, StringComparison.OrdinalIgnoreCase) >= 0
                || h.Name.IndexOf(inner, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return h.Name;
            }
        }

        foreach (var h in headsets)
        {
            var tokens = h.Name.Split(new[] { ' ', '\'', '#' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 4)
                .ToArray();
            if (tokens.Length > 0 && tokens.All(t =>
                    endpoint.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                    || inner.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return h.Name;
            }
        }

        return null;
    }
}
