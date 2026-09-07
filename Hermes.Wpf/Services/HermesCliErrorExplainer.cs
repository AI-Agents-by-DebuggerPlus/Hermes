namespace Hermes.Wpf.Services;

public sealed class HermesCliErrorHelp
{
    public required string Title { get; init; }
    public required string RawError { get; init; }
    public required string Explanation { get; init; }
    public required string FixInstructions { get; init; }
}

/// <summary>Maps Hermes/WSL/OpenRouter failure text to Russian explanation + fix steps.</summary>
public static class HermesCliErrorExplainer
{
    public static HermesCliErrorHelp Explain(string? rawError, int? exitCode = null)
    {
        var raw = (rawError ?? string.Empty).Trim();
        if (raw.Length > 600)
            raw = raw[..600] + "…";

        if (ContainsAny(raw, "Key limit exceeded", "total limit"))
        {
            return new HermesCliErrorHelp
            {
                Title = "Лимит ключа OpenRouter",
                RawError = raw,
                Explanation =
                    "Баланс аккаунта OpenRouter мог быть пополнен, но у конкретного API-ключа "
                    + "стоит отдельный spending limit. Hermes использует этот ключ — запросы "
                    + "отклоняются с HTTP 403, пока лимит ключа не поднят или не создан новый ключ.",
                FixInstructions =
                    "1. Откройте https://openrouter.ai/keys\n"
                    + "2. Найдите ключ из ошибки (или текущий в ~/hermes-agent/.env → OPENROUTER_API_KEY).\n"
                    + "3. Увеличьте Limit / Credit limit для ключа — или создайте новый ключ без жёсткого лимита.\n"
                    + "4. Если новый ключ: вставьте его в WSL-файл ~/hermes-agent/.env (OPENROUTER_API_KEY=…).\n"
                    + "5. В Hermes.Wpf нажмите Retry last (или отправьте сообщение снова).",
            };
        }

        if (ContainsAny(raw, "HTTP 401", "Unauthorized", "invalid api key", "Incorrect API key"))
        {
            return new HermesCliErrorHelp
            {
                Title = "Неверный API-ключ",
                RawError = raw,
                Explanation =
                    "Провайдер (обычно OpenRouter) отклонил ключ: он отсутствует, отозван или "
                    + "не совпадает с тем, что в конфигурации Hermes CLI.",
                FixInstructions =
                    "1. Проверьте ключ на https://openrouter.ai/keys\n"
                    + "2. Обновите OPENROUTER_API_KEY в ~/hermes-agent/.env\n"
                    + "3. При необходимости выполните: hermes setup\n"
                    + "4. Повторите запрос (Retry last).",
            };
        }

        if (ContainsAny(raw, "HTTP 429", "rate limit", "Rate limited", "Too Many Requests"))
        {
            return new HermesCliErrorHelp
            {
                Title = "Слишком много запросов (rate limit)",
                RawError = raw,
                Explanation =
                    "Провайдер временно ограничил частоту запросов. Это не исчерпание баланса, "
                    + "а пауза из‑за лимита RPS/квоты.",
                FixInstructions =
                    "1. Подождите 1–2 минуты.\n"
                    + "2. Повторите сообщение (Retry last).\n"
                    + "3. Если повторяется часто — смените модель в ~/.hermes/config.yaml "
                    + "или проверьте лимиты на openrouter.ai.",
            };
        }

        if (ContainsAny(raw, "HTTP 403") && ContainsAny(raw, "openrouter", "Provider:", "API call failed"))
        {
            return new HermesCliErrorHelp
            {
                Title = "Доступ к API запрещён (HTTP 403)",
                RawError = raw,
                Explanation =
                    "OpenRouter (или другой провайдер) отклонил запрос. Часто это лимит ключа, "
                    + "ограничение модели или политики аккаунта.",
                FixInstructions =
                    "1. Откройте текст ошибки и ссылку openrouter.ai (если есть).\n"
                    + "2. Проверьте Keys / Credits / доступность модели.\n"
                    + "3. Обновите ключ в ~/hermes-agent/.env при необходимости.\n"
                    + "4. Retry last.",
            };
        }

        if (ContainsAny(raw, "Failed to start the systemd user session", "systemd user session"))
        {
            return new HermesCliErrorHelp
            {
                Title = "WSL не поднял user-сессию systemd",
                RawError = raw,
                Explanation =
                    "Дистрибутив Ubuntu/WSL только запускался: user systemd ещё не готов, "
                    + "поэтому hermes chat завершился с ошибкой. Обычно это одноразовый сбой "
                    + "холодного старта.",
                FixInstructions =
                    "1. Подождите 5–10 секунд.\n"
                    + "2. Нажмите Retry last.\n"
                    + "3. Если повторяется: wsl -d Ubuntu -- bash -lc \"systemctl --user is-system-running\"\n"
                    + "4. Убедитесь, что в Settings указан Wsl Distro = Ubuntu (не docker-desktop).",
            };
        }

        if (ContainsAny(raw, "Timed out", "timeout", "Превышено время ожидания"))
        {
            return new HermesCliErrorHelp
            {
                Title = "Таймаут ожидания Hermes",
                RawError = raw,
                Explanation =
                    "Ответ CLI не успел прийти за отведённое время (долгий tool-call, модель или зависание WSL).",
                FixInstructions =
                    "1. Settings → увеличьте Chat Timeout (например 600–1800 с).\n"
                    + "2. Проверьте, что WSL отвечает: wsl -d Ubuntu -- bash -lc \"hermes status\".\n"
                    + "3. Retry last.",
            };
        }

        if (ContainsAny(raw, "command not found", "No such file", "venv", "activate"))
        {
            return new HermesCliErrorHelp
            {
                Title = "Hermes CLI / venv не найдены",
                RawError = raw,
                Explanation =
                    "В WSL не активировалось окружение или команда hermes недоступна в PATH.",
                FixInstructions =
                    "1. Settings → Venv Path (обычно ~/hermes-agent/venv).\n"
                    + "2. В Ubuntu: source ~/hermes-agent/venv/bin/activate && hermes --version\n"
                    + "3. При необходимости переустановите/почините hermes-agent.",
            };
        }

        var title = exitCode is > 0
            ? $"Ошибка Hermes CLI (exit {exitCode})"
            : "Ошибка Hermes CLI";

        return new HermesCliErrorHelp
        {
            Title = title,
            RawError = string.IsNullOrWhiteSpace(raw) ? "(нет текста ошибки)" : raw,
            Explanation =
                "Hermes CLI завершился с ошибкой. Ниже — исходный текст; "
                + "расшифровка для этого сообщения пока общая.",
            FixInstructions =
                "1. Посмотрите Terminal и session-лог Hermes.Wpf.\n"
                + "2. В WSL: hermes status\n"
                + "3. Исправьте причину (API, WSL, сеть) и нажмите Retry last.",
        };
    }

    private static bool ContainsAny(string text, params string[] cues)
    {
        foreach (var cue in cues)
        {
            if (text.Contains(cue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
