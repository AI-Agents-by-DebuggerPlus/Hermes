using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Sends structured local execution results back to Hermes CLI for memory, reflection, and skill crystallization.</summary>
public sealed class CliLearningFollowUpService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly HermesService _hermes;
    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;

    public CliLearningFollowUpService(
        HermesService hermes,
        LogService log,
        Func<HermesSettings> settings)
    {
        _hermes = hermes;
        _log = log;
        _settings = settings;
    }

    public bool IsEnabled => _settings().CliPostLocalFollowUpEnabled;

    public async Task<string?> SendAsync(
        string userTask,
        WpfLocalActionResult result,
        string wslWorkDir,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var settings = _settings();
        var payload = BuildFollowUpPrompt(userTask, result);
        var timeout = Math.Clamp(settings.ChatTimeoutSeconds, 30, 300);

        _log.LogInfo($"[cli-follow-up] action={result.Action} ok={result.Ok}");

        var cliResult = await _hermes
            .SendMessageAsync(payload, wslWorkDir, settings, timeout)
            .ConfigureAwait(false);

        if (!cliResult.Success)
        {
            _log.LogWarn($"[cli-follow-up] CLI exit {cliResult.ExitCode}: {cliResult.LastStderrLine}");
            return null;
        }

        var text = (cliResult.CombinedText ?? string.Empty).Trim();
        return text.Length == 0 ? null : text;
    }

    internal static string BuildFollowUpPrompt(string userTask, WpfLocalActionResult result)
    {
        var structured = JsonSerializer.Serialize(
            new
            {
                hook = "post_local_execution",
                user_task = userTask,
                action = result.Action,
                ok = result.Ok,
                message = result.UserMessage,
                timestamp_utc = DateTime.UtcNow.ToString("O"),
            },
            JsonOptions);

        return (userTask ?? string.Empty).Trim()
               + "\n\n---\n[System / Hermes WPF — post-local execution hook]\n"
               + "Hermes.Wpf выполнил локальное действие Windows по твоему предыдущему JSON `wpf_local`.\n"
               + "Структурированный результат:\n```json\n"
               + structured
               + "\n```\n"
               + "Задачи:\n"
               + "1. Кратко подтверди пользователю итог (на русском).\n"
               + "2. Если есть урок для долгосрочной памяти — предложи сохранение или верни skill_save при повторяющемся паттерне.\n"
               + "3. НЕ повторяй wpf_local без новой явной просьбы пользователя.\n"
               + "4. Не утверждай, что выполнял Playwright сам — это сделал клиент Windows.";
    }
}
