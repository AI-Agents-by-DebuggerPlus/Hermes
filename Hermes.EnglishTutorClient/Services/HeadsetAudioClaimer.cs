using System;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Hermes.EnglishTutorClient.Services;

/// <summary>
/// On startup: prefer BT headset for render + Hands-Free for capture,
/// set Windows defaults, keep a near-silent render session so the profile stays alive.
/// </summary>
public sealed class HeadsetAudioClaimer : IDisposable
{
    private MMDeviceEnumerator? _enum;
    private WasapiOut? _holdOut;
    private MMDevice? _render;
    private MMDevice? _capture;
    private bool _disposed;

    public string? RenderName => _render?.FriendlyName;
    public string? CaptureName => _capture?.FriendlyName;

    /// <summary>Invoked after hold resume / reclaim completes (wire SMTC reassert from MainWindow).</summary>
    public Action? AfterHoldResumed { get; set; }

    public void Claim()
    {
        try
        {
            _enum ??= new MMDeviceEnumerator();
            RefreshDevices();

            if (_render != null && IsPreferredRender(_render))
            {
                AudioPolicyConfig.TryClaimAllRoles(_render);
                StartHoldPlayback(_render);
            }
            else if (_render != null)
            {
                // Speakers only — hold session without fighting user defaults hard
                StartHoldPlayback(_render);
                AppLog.Info("HeadsetAudioClaim: no BT render yet, hold on " + _render.FriendlyName);
            }

            if (_capture != null && IsHandsFree(_capture))
            {
                AudioPolicyConfig.TryClaimAllRoles(_capture);
            }
            else
            {
                AppLog.Warn("HeadsetAudioClaim: Hands-Free not Active — skip capture default"
                    + ( _capture != null ? " (would be " + _capture.FriendlyName + ")" : ""));
                _capture = null;
            }

            Thread.Sleep(150);
            LogDefaults();
            try
            {
                var c = SafeDefault(_enum, DataFlow.Capture, Role.Communications);
                var cn = c?.FriendlyName ?? "";
                if (cn.IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) >= 0
                    || cn.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppLog.Warn("HeadsetAudioClaim: Communications still '" + cn
                        + "' — will reclaim when BT Hands-Free wakes");
                }
            }
            catch { /* ignore */ }

            AppLog.Info("HeadsetAudioClaim OUT=" + (RenderName ?? "(none)")
                + " IN=" + (CaptureName ?? "(none)"));
        }
        catch (Exception ex)
        {
            AppLog.Warn("HeadsetAudioClaim: " + ex.Message);
        }
    }

    private static bool IsHandsFree(MMDevice d) =>
        (d.FriendlyName ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0
        && (d.FriendlyName ?? "").IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) < 0;

    private static bool IsPreferredRender(MMDevice d)
    {
        var n = d.FriendlyName ?? "";
        return n.IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Headphones", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0
               || n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void Reclaim()
    {
        if (_disposed) return;
        Claim();
    }

    /// <summary>
    /// Stop A2DP Stereo hold so BT can switch to Hands-Free SCO (otherwise mic peak stays 0).
    /// </summary>
    public void PauseHoldForCapture()
    {
        if (_disposed) return;
        StopHold();
        AppLog.Info("HeadsetAudioClaim hold paused for mic (release A2DP Stereo)");
    }

    public void ResumeHoldAfterCapture()
    {
        if (_disposed) return;
        try
        {
            _enum ??= new MMDeviceEnumerator();
            RefreshDevices();
            if (_render != null)
                StartHoldPlayback(_render);
            AppLog.Info("HeadsetAudioClaim hold resumed");
            try { AfterHoldResumed?.Invoke(); }
            catch (Exception ex) { AppLog.Warn("AfterHoldResumed: " + ex.Message); }
        }
        catch (Exception ex)
        {
            AppLog.Warn("HeadsetAudioClaim resume hold: " + ex.Message);
        }
    }

    /// <summary>Hands-Free render endpoint (SCO) — pair with Hands-Free capture for mic.</summary>
    public MMDevice? TryGetHandsFreeRender()
    {
        try
        {
            _enum ??= new MMDeviceEnumerator();
            return PreferHandsFreeRender(_enum);
        }
        catch { return null; }
    }

    private static MMDevice? PreferHandsFreeRender(MMDeviceEnumerator en)
    {
        return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged)
            .Where(d => (d.FriendlyName ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(d => d.State == DeviceState.Active ? 0 : 1)
            .ThenByDescending(d => (d.FriendlyName ?? "").IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
    }

    private void RefreshDevices()
    {
        if (_enum == null) return;

        _render = PreferHeadsetRender(_enum)
                  ?? SafeDefault(_enum, DataFlow.Render, Role.Multimedia)
                  ?? SafeDefault(_enum, DataFlow.Render, Role.Console);

        _capture = PreferHandsFree(_enum);
        // Do NOT fall back to iVCam / random default for claiming.

        // Re-resolve by ID so MMDevice stays valid with long-lived enumerator
        if (_render != null)
            _render = _enum.GetDevice(_render.ID);
        if (_capture != null)
            _capture = _enum.GetDevice(_capture.ID);
    }

    private void StartHoldPlayback(MMDevice render)
    {
        try
        {
            StopHold();
            var silence = new SignalGenerator(44100, 1)
            {
                Gain = 0.0001,
                Frequency = 20,
                Type = SignalGeneratorType.Sin,
            };
            _holdOut = new WasapiOut(render, AudioClientShareMode.Shared, true, 200);
            _holdOut.Init(silence);
            _holdOut.Play();
        }
        catch (Exception ex)
        {
            AppLog.Warn("HeadsetAudioClaim hold playback failed: " + ex.Message);
            StopHold();
        }
    }

    private void StopHold()
    {
        try { _holdOut?.Stop(); } catch { /* ignore */ }
        try { _holdOut?.Dispose(); } catch { /* ignore */ }
        _holdOut = null;
    }

    private void LogDefaults()
    {
        if (_enum == null) return;
        try
        {
            var r = SafeDefault(_enum, DataFlow.Render, Role.Multimedia);
            var c = SafeDefault(_enum, DataFlow.Capture, Role.Communications);
            AppLog.Info("HeadsetAudioClaim defaults OUT=" + (r?.FriendlyName ?? "?")
                + " IN-comm=" + (c?.FriendlyName ?? "?"));
        }
        catch { /* ignore */ }
    }

    private static MMDevice? PreferHeadsetRender(MMDeviceEnumerator en)
    {
        return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Where(d =>
            {
                var n = d.FriendlyName ?? "";
                return n.IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Headphones", StringComparison.OrdinalIgnoreCase) >= 0
                       || n.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0;
            })
            .OrderBy(d => (d.FriendlyName ?? "").IndexOf("Stereo", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
            .ThenByDescending(d => (d.FriendlyName ?? "").IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
    }

    private static MMDevice? PreferHandsFree(MMDeviceEnumerator en)
    {
        var all = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged)
            .Where(d => (d.FriendlyName ?? "").IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(d => (d.FriendlyName ?? "").IndexOf("iVCam", StringComparison.OrdinalIgnoreCase) < 0)
            .ToList();

        // Never promote Unplugged to default — Windows ignores / reverts to iVCam.
        var active = all.Where(d => d.State == DeviceState.Active)
            .OrderByDescending(d => (d.FriendlyName ?? "").IndexOf("Pixel", StringComparison.OrdinalIgnoreCase) >= 0)
            .FirstOrDefault();
        return active;
    }

    private static MMDevice? SafeDefault(MMDeviceEnumerator en, DataFlow flow, Role role)
    {
        try { return en.GetDefaultAudioEndpoint(flow, role); }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopHold();
        try { _enum?.Dispose(); } catch { /* ignore */ }
        _enum = null;
        _render = null;
        _capture = null;
    }
}
