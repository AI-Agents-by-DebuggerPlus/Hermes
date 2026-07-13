using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Services;

public sealed class HeadsetBatteryInfo
{
    public HeadsetBatteryInfo(string name, int percent)
    {
        Name = name;
        Percent = percent;
    }

    public string Name { get; }
    public int Percent { get; }
}

/// <summary>
/// Reads Bluetooth headset battery from Windows PnP
/// (same property Settings uses: {104EA319-…} 2).
/// </summary>
public static class BluetoothHeadsetBatteryReader
{
    private static readonly Regex SuffixCleanup = new(
        @"\s*(Hands-Free AG Audio|Hands-Free AG|Hands-Free|Stereo|Avrcp Transport)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Task<IReadOnlyList<HeadsetBatteryInfo>> ReadAsync() =>
        Task.Run(ReadCore);

    private static IReadOnlyList<HeadsetBatteryInfo> ReadCore()
    {
        string? scriptPath = null;
        try
        {
            scriptPath = Path.Combine(Path.GetTempPath(), "hermes_bt_battery_" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(scriptPath, @"
$ErrorActionPreference = 'SilentlyContinue'
$out = New-Object System.Collections.Generic.List[object]
Get-PnpDevice -PresentOnly |
  Where-Object { $_.InstanceId -like 'BTHENUM\*' -and $_.FriendlyName -like '*Hands-Free AG*' } |
  ForEach-Object {
    $dev = $_
    Get-PnpDeviceProperty -InstanceId $dev.InstanceId |
      Where-Object { $_.KeyName -like '{104EA319-6EE2-4701-BD47-8DDBF425BBE5}*' -and $_.Type.ToString() -eq 'Byte' } |
      ForEach-Object {
        $n = 0
        if ([int]::TryParse([string]$_.Data, [ref]$n) -and $n -ge 0 -and $n -le 100) {
          $out.Add([PSCustomObject]@{ Name = $dev.FriendlyName; Percent = $n }) | Out-Null
        }
      }
  }
if ($out.Count -eq 0) { Write-Output '[]' }
elseif ($out.Count -eq 1) { Write-Output ($out[0] | ConvertTo-Json -Compress) }
else { Write-Output ($out | ConvertTo-Json -Compress) }
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
                AppLog.Warn("BT battery: failed to start powershell");
                return Array.Empty<HeadsetBatteryInfo>();
            }

            var json = proc.StandardOutput.ReadToEnd() ?? string.Empty;
            var err = proc.StandardError.ReadToEnd() ?? string.Empty;
            if (!proc.WaitForExit(25000))
            {
                try { proc.Kill(); } catch { }
                AppLog.Warn("BT battery: powershell timeout");
                return Array.Empty<HeadsetBatteryInfo>();
            }

            json = json.Trim();
            AppLog.Info("BT battery raw exit=" + proc.ExitCode + " stdoutLen=" + json.Length
                + (string.IsNullOrWhiteSpace(err) ? string.Empty : " stderr=" + Truncate(err, 160)));

            if (string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                AppLog.Warn("BT battery: no Hands-Free AG devices with battery property (is headset connected?)");
                return Array.Empty<HeadsetBatteryInfo>();
            }

            var list = new List<HeadsetBatteryInfo>();
            if (json.StartsWith("[", StringComparison.Ordinal))
            {
                foreach (var item in JArray.Parse(json))
                {
                    Add(list, (string?)item["Name"], item["Percent"]?.Value<int?>());
                }
            }
            else if (json.StartsWith("{", StringComparison.Ordinal))
            {
                var obj = JObject.Parse(json);
                Add(list, (string?)obj["Name"], obj["Percent"]?.Value<int?>());
            }
            else
            {
                AppLog.Warn("BT battery: unexpected JSON: " + Truncate(json, 200));
            }

            var byName = new Dictionary<string, HeadsetBatteryInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                var clean = CleanName(item.Name);
                if (string.IsNullOrWhiteSpace(clean))
                {
                    continue;
                }

                if (!byName.TryGetValue(clean, out var existing) || item.Percent > existing.Percent)
                {
                    byName[clean] = new HeadsetBatteryInfo(clean, item.Percent);
                }
            }

            var result = byName.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            AppLog.Info("BT battery parsed: " + (result.Count == 0
                ? "none"
                : string.Join("; ", result.Select(r => r.Name + "=" + r.Percent + "%"))));
            return result;
        }
        catch (Exception ex)
        {
            AppLog.Warn("BT battery read failed: " + ex.Message);
            return Array.Empty<HeadsetBatteryInfo>();
        }
        finally
        {
            if (scriptPath != null)
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
    }

    private static void Add(List<HeadsetBatteryInfo> list, string? name, int? percent)
    {
        if (string.IsNullOrWhiteSpace(name) || !percent.HasValue)
        {
            return;
        }

        var p = percent.Value;
        if (p < 0 || p > 100)
        {
            return;
        }

        list.Add(new HeadsetBatteryInfo(name!.Trim(), p));
    }

    public static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var n = raw!.Trim();
        n = SuffixCleanup.Replace(n, string.Empty).Trim();
        while (n.Length > 0 && (n[0] == '#' || n[0] == '\'' || n[0] == ' '))
        {
            n = n.Substring(1).TrimStart();
        }

        return n;
    }

    public static string FormatStatus(IReadOnlyList<HeadsetBatteryInfo> devices)
    {
        if (devices == null || devices.Count == 0)
        {
            return "BT: —";
        }

        return string.Join(" | ", devices.Select(d => d.Name + " " + d.Percent + "%"));
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}
