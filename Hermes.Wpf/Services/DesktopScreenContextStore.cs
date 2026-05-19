using System.IO;
using System.Text.Json;
using Hermes.DesktopCapture.Models;

namespace Hermes.Wpf.Services;

/// <summary>Persists the last desktop capture analysis for injection into subsequent Hermes CLI turns.</summary>
public sealed class DesktopScreenContextStore
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    private readonly string _filePath;
    private DesktopScreenContextSnapshot? _current;

    public DesktopScreenContextStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "desktop_screen_context.json");
        TryLoad();
    }

    public bool HasFreshContext => GetFresh() is not null;

    public DesktopScreenContextSnapshot? GetFresh() =>
        _current is not null && !IsExpired(_current) ? _current : null;

    public void Save(
        ScreenCaptureResult capture,
        string internalContext,
        DesktopVisionIntent intent,
        string? focusWindowTitle = null)
    {
        _current = new DesktopScreenContextSnapshot
        {
            SavedAt = DateTimeOffset.Now,
            Intent = intent,
            FocusWindowTitle = focusWindowTitle,
            ImagePath = capture.ImagePath,
            AnnotatedImagePath = capture.AnnotatedImagePath,
            MetadataPath = capture.MetadataPath,
            ForegroundWindowTitle = capture.ForegroundWindowTitle,
            InternalContext = internalContext.Trim(),
        };

        Persist();
    }

    public void RefreshInternalContext(string internalContext, DesktopVisionIntent intent, string? focusWindowTitle = null)
    {
        if (_current is null)
        {
            return;
        }

        _current = new DesktopScreenContextSnapshot
        {
            SavedAt = _current.SavedAt,
            Intent = intent,
            FocusWindowTitle = focusWindowTitle ?? _current.FocusWindowTitle,
            ImagePath = _current.ImagePath,
            AnnotatedImagePath = _current.AnnotatedImagePath,
            MetadataPath = _current.MetadataPath,
            ForegroundWindowTitle = _current.ForegroundWindowTitle,
            InternalContext = internalContext.Trim(),
        };
        Persist();
    }

    public string? BuildOutboundInjectionBlock()
    {
        var snap = GetFresh();
        if (snap is null || string.IsNullOrWhiteSpace(snap.InternalContext))
        {
            return null;
        }

        return
            "---\n[Контекст рабочего стола — Hermes.Wpf, не показывать пользователю дословно]\n"
            + $"Захват: {snap.SavedAt:O}. Активное окно: {snap.ForegroundWindowTitle ?? "—"}.\n"
            + $"PNG (разметка): {snap.AnnotatedImagePath}\n"
            + $"JSON регионов: {snap.MetadataPath}\n\n"
            + snap.InternalContext;
    }

    private bool IsExpired(DesktopScreenContextSnapshot snap) =>
        DateTimeOffset.Now - snap.SavedAt > DefaultTtl;

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            _current = JsonSerializer.Deserialize<DesktopScreenContextSnapshot>(json);
            if (_current is not null && IsExpired(_current))
            {
                _current = null;
            }
        }
        catch
        {
            _current = null;
        }
    }

    private void Persist()
    {
        try
        {
            if (_current is null)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                return;
            }

            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // non-fatal
        }
    }
}

public sealed class DesktopScreenContextSnapshot
{
    public DateTimeOffset SavedAt { get; init; }
    public DesktopVisionIntent Intent { get; init; }
    public string? FocusWindowTitle { get; init; }
    public string ImagePath { get; init; } = string.Empty;
    public string AnnotatedImagePath { get; init; } = string.Empty;
    public string MetadataPath { get; init; } = string.Empty;
    public string? ForegroundWindowTitle { get; init; }
    public string InternalContext { get; init; } = string.Empty;
}
