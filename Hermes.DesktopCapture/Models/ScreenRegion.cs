namespace Hermes.DesktopCapture.Models;

public sealed record ScreenRegion
{
    public required string Id { get; init; }

    public required ScreenRegionRole Role { get; init; }

    /// <summary>Short role label (e.g. «Закрыть», «Панель заголовка»).</summary>
    public required string Label { get; init; }

    /// <summary>1-based index on annotated screenshot.</summary>
    public int Index { get; init; }

    /// <summary>Prefix of owning window (<c>w0</c>, <c>w1</c>).</summary>
    public string? WindowPrefix { get; init; }

    public string? OwnerWindowTitle { get; init; }

    public string? OwnerProcessName { get; init; }

    /// <summary>«Приложение …» when resolved.</summary>
    public string? OwnerApplicationDisplay { get; init; }

    /// <summary>«Окно …» for child regions and fallback for windows.</summary>
    public string? OwnerWindowDisplay { get; init; }

    /// <summary>Full name for chat / JSON (<c>Окно … — Закрыть</c>).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Screen coordinates in physical pixels.</summary>
    public required int X { get; init; }

    public required int Y { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public System.Drawing.Rectangle ToRectangle() => new(X, Y, Width, Height);
}
