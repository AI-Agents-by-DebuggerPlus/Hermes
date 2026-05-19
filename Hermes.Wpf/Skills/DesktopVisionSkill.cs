using System.IO;
using Hermes.DesktopCapture.Models;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Skills;

public sealed class DesktopVisionSkill
{
    private readonly HermesService _hermes;
    private readonly LogService _log;
    private readonly ProjectService _projects;
    private readonly Func<HermesSettings> _settings;

    public DesktopVisionSkill(
        HermesService hermes,
        LogService log,
        ProjectService projects,
        Func<HermesSettings> settings)
    {
        _hermes = hermes;
        _log = log;
        _projects = projects;
        _settings = settings;
    }

    public bool IsEnabled => _settings().DesktopVisionAnalyzeEnabled;

    public Task<DesktopVisionAnalysisResult> AnalyzeCaptureAsync(
        ScreenCaptureResult capture,
        string wslWorkDir,
        string outboundPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.FromResult(DesktopVisionAnalysisResult.Skip());
        }

        var annotated = _projects.ConvertToWslPath(capture.AnnotatedImagePath);
        var plain = _projects.ConvertToWslPath(capture.ImagePath);
        if (string.IsNullOrEmpty(annotated) || string.IsNullOrEmpty(plain))
        {
            return Task.FromResult(
                DesktopVisionAnalysisResult.Fail("Не удалось преобразовать путь к изображению для WSL."));
        }

        _log.LogInfo($"[desktop-vision] hermes chat: annotated={annotated}, plain={plain}");
        return RunVisionPromptAsync(wslWorkDir, outboundPrompt, cancellationToken);
    }

    public async Task<DesktopVisionAnalysisResult> RunVisionPromptAsync(
        string wslWorkDir,
        string outboundPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return DesktopVisionAnalysisResult.Skip();
        }

        var timeout = ClampTimeout(_settings().ChatTimeoutSeconds);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(timeout));

        try
        {
            var result = await _hermes
                .SendMessageAsync(outboundPrompt, wslWorkDir, _settings(), timeout)
                .ConfigureAwait(false);

            if (linked.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return DesktopVisionAnalysisResult.Fail(
                    $"Таймаут анализа скриншота ({timeout} с). Увеличьте Chat timeout в настройках.");
            }

            if (!result.Success)
            {
                var hint = string.IsNullOrWhiteSpace(result.CombinedText)
                    ? result.LastStderrLine ?? "неизвестная ошибка"
                    : result.CombinedText;
                _log.LogWarn($"[desktop-vision] exit={result.ExitCode}: {hint}");
                return DesktopVisionAnalysisResult.Fail($"Hermes CLI (exit {result.ExitCode}): {hint}");
            }

            var text = string.IsNullOrWhiteSpace(result.CombinedText)
                ? "(пустой ответ Hermes)"
                : result.CombinedText.Trim();
            _log.LogInfo($"[desktop-vision] ok, chars={text.Length}");
            var parsed = DesktopVisionResponseParser.Parse(text);
            return DesktopVisionAnalysisResult.Ok(parsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError($"[desktop-vision] {ex}");
            return DesktopVisionAnalysisResult.Fail(ex.Message);
        }
    }

    public string ResolveVisionImageWindowsPath(ScreenCaptureResult capture)
    {
        var settings = _settings();
        var path = settings.DesktopVisionUseAnnotatedImage
            ? capture.AnnotatedImagePath
            : capture.ImagePath;
        return Path.GetFullPath(path);
    }

    private static int ClampTimeout(int seconds)
    {
        const int minS = 60;
        const int maxS = 7200;
        if (seconds < minS)
        {
            return minS;
        }

        return seconds > maxS ? maxS : seconds;
    }
}

public sealed class DesktopVisionAnalysisResult
{
    public bool Skipped { get; init; }
    public bool Success { get; init; }
    public string? RawText { get; init; }
    public string? InternalContext { get; init; }
    public string? UserVisible { get; init; }
    public string? Error { get; init; }

    public static DesktopVisionAnalysisResult Skip() => new() { Skipped = true };

    public static DesktopVisionAnalysisResult Ok(DesktopVisionParsedResponse parsed) =>
        new()
        {
            Success = true,
            RawText = parsed.Raw,
            InternalContext = parsed.InternalContext ?? parsed.Raw,
            UserVisible = parsed.UserVisible,
        };

    public static DesktopVisionAnalysisResult Fail(string error) =>
        new() { Success = false, Error = error };
}
