using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Ensures on-disk project layout (<c>hermes/</c>, <c>AGENTS.md</c>) for Hermes CLI.
/// Agent memory stays in <c>~/.hermes/</c> — not created here.
/// </summary>
public sealed class ProjectAgentsBootstrapService
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private const string MemorySeparationMarker = "## Разделение памяти и файлов проекта";
    private const string ConciseReplyMarker = "## Краткость ответа";
    private const string AndroidChatSectionMarker = "## Supabase → AndroidChat";
    private const string LegacyAndroidTtsSectionMarker = "## Supabase → Android TTS";
    private const string VoiceProtocolMarker = "[Voice]";

    private const string AndroidChatSectionBody = """
        ## Supabase → AndroidChat

        Ответы синхронизируются в Supabase. AndroidChat (≥ 1.0.41) озвучивает **только** текст внутри `[Voice]…[/Voice]`.
        Без оболочки Voice озвучки нет (даже при `{"en":…}`).

        ```
        [info]
        Текст для чтения (можно подробнее по запросу).

        [Voice]
        {"ru":"Кратко по сути."}
        {"en":"Hermes"}
        [/Voice]
        ```

        Внутри Voice: объекты с `"ru"` / `"en"`; одно предложение ≈ одна JSON-строка; без markdown / `-` / `—`.
        Служебный текст, diff, `file://` — **вне** Voice. Legacy `[speak]` не использовать.

        """;

    private const string AgentsTemplate = """
        # Hermes — правила проекта

        ## Разделение памяти и файлов проекта

        Как у разработчика: **на диске** — всё, что относится к *этому* проекту; **в `~/.hermes/`** — обобщённые знания, опыт и skills агента Hermes (Nous).

        ### На диске (эта папка проекта)

        | Путь | Назначение |
        |------|------------|
        | `hermes/project.md` | URL, шаги, задачи и статус **этого** проекта |
        | `hermes/screenshots/` | PNG скриншоты **этого** проекта (`HERMES_SCREENSHOT_DIR`) |
        | `hermes/credentials.md` | Учётные данные проекта (не коммитить в git) |
        | `AGENTS.md` | Правила работы в этом проекте (этот файл) |

        ### В памяти агента (`~/.hermes/`)

        | Путь | Назначение |
        |------|------------|
        | `memories/MEMORY.md`, `USER.md` | Переносимый опыт: браузер, логин, скриншоты, отладка |
        | `skills/` | Переиспользуемые skills (`hermes skills`) с triggers |

        **Правило:** данные проекта (конкретные URL, расписание, статус «водоканал») → `hermes/project.md`. Успешный **обобщаемый** приём (навигация, auth, screenshot tool) → memory/skill в `~/.hermes/`. Не дублируй project-specific факты в `MEMORY.md`.

        ## Краткость ответа

        - По умолчанию отвечай **2–3 предложениями** по сути текущего вопроса.
        - Без длинных markdown-списков, без обзоров «на всякий случай».
        - Развёрнутый ответ — **только** если пользователь явно просит подробности («подробнее», «разверни», «объясни детально», «полный разбор» и аналоги).

        """
        + AndroidChatSectionBody
        + """
        ## Приоритет текущего запроса (строго)

        - **Всегда выполняй только текущий запрос пользователя.** Контекст читай из `hermes/project.md` и `~/.hermes/`, только если это нужно для *текущего* запроса.
        - **Не повторяй прошлые задачи**, если пользователь явно не просил «продолжи / как раньше».
        - При конфликте — **текущий запрос**; спроси недостающие данные.

        ## Задачи этого проекта

        - Новые задачи, URL, шаги, статус → секция `## Задачи` в `hermes/project.md`.
        - «Какие задачи по проекту» → читай `hermes/project.md`, не глобальный `MEMORY.md`.
        - «Продолжи» → `hermes/project.md` + при необходимости `--resume` сессию.

        ## Скриншоты сайта (browser)

        - PNG **только** в `$HERMES_SCREENSHOT_DIR` (каталог `hermes/screenshots/` проекта) — пути **WSL/Linux**, не `C:\...`.
        - «Только скриншот»: URL из `hermes/project.md`, **без** логина/ввода/отправки форм, если пользователь не просил иное.
        - В ответе — **полный путь** к PNG.

        ## Skills (только CLI)

        - Skills через `hermes skills` в `~/.hermes/skills/` после успешного обобщаемого сценария.
        - **Не** используй `builtin_*`, `wpf_local`.
        - Tool `browser` — до появления skill или как fallback.

        ## Учётные данные

        - Пароли проекта → `hermes/credentials.md` (или ссылка «см. сообщение от ДАТА»), не в `MEMORY.md` и не в чат без необходимости.

        """;

    private const string ConciseAndTtsAppend = """

        ## Краткость ответа

        - По умолчанию отвечай **2–3 предложениями** по сути текущего вопроса.
        - Без длинных markdown-списков, без обзоров «на всякий случай».
        - Развёрнутый ответ — **только** если пользователь явно просит подробности («подробнее», «разверни», «объясни детально», «полный разбор» и аналоги).

        """
        + "\n"
        + AndroidChatSectionBody;

    private const string MemorySeparationAppend = """

        ## Разделение памяти и файлов проекта

        **Диск:** `hermes/project.md` (задачи, URL), `hermes/screenshots/` (PNG). **Агент:** `~/.hermes/memories/`, `~/.hermes/skills/` — только обобщённый опыт; project-specific факты не писать в `MEMORY.md`.

        """;

    private const string ProjectDataTemplate = """
        # Данные проекта (диск)

        Этот файл — артефакты **этого** проекта на диске. Не путать с `~/.hermes/memories/MEMORY.md` (память агента Hermes).

        ## Задачи

        | id | суть | статус | дата |
        |----|------|--------|------|
        | | | pending | |

        ## URL и страницы

        - Вход (логин):
        - Ввод показаний / основная форма:
        - Прочее:

        ## Скриншоты

        Каталог: `hermes/screenshots/` (переменная `HERMES_SCREENSHOT_DIR` при запуске из Hermes.Wpf).

        ## Заметки

        -

        """;

    private const string HermesReadmeTemplate = """
        # Hermes — файлы проекта на диске

        Здесь хранятся данные **этого** проекта (как файлы в репозитории).

        Память агента Hermes CLI (`~/.hermes/memories/`, `~/.hermes/skills/`) — отдельно: навыки и опыт, переносимые между проектами.

        - `project.md` — URL, задачи, статус
        - `screenshots/` — скриншоты браузера
        - `credentials.md` — учётные данные (добавьте в `.gitignore`)

        """;

    private const string CredentialsTemplate = """
        # Учётные данные (только этот проект)

        **Не коммитьте** этот файл в git. Добавьте `hermes/credentials.md` в `.gitignore`.

        - Логин:
        - Пароль: (или «см. менеджер паролей / сообщение от ДАТА»)

        """;

    private static readonly Regex AndroidChatSectionRegex = new(
        @"##\s*Supabase\s*→\s*Android(?:Chat|TTS)[\s\S]*?(?=\r?\n##\s|\z)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly LogService _log;

    public ProjectAgentsBootstrapService(LogService log) => _log = log;

    /// <summary>Creates <c>hermes/</c> layout and <c>AGENTS.md</c> if missing.</summary>
    public void EnsureProjectHermesArtifacts(string? projectWindowsPath)
    {
        var root = (projectWindowsPath ?? string.Empty).Trim();
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return;
        }

        EnsureProjectLayout(root);
        EnsureAgentsFile(root);
    }

    private void EnsureProjectLayout(string projectRoot)
    {
        var hermesDir = HermesProjectLayout.GetHermesDirectory(projectRoot);
        Directory.CreateDirectory(HermesProjectLayout.GetScreenshotsDirectory(projectRoot));

        WriteIfMissing(Path.Combine(hermesDir, HermesProjectLayout.ReadmeFileName), HermesReadmeTemplate);
        WriteIfMissing(HermesProjectLayout.GetProjectDataPath(projectRoot), ProjectDataTemplate);
        WriteIfMissing(Path.Combine(hermesDir, HermesProjectLayout.CredentialsFileName), CredentialsTemplate);

        var gitignorePath = Path.Combine(projectRoot, ".gitignore");
        TryAppendGitignoreLine(gitignorePath, "hermes/credentials.md");
    }

    private void EnsureAgentsFile(string projectRoot)
    {
        var path = Path.Combine(projectRoot, "AGENTS.md");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, AgentsTemplate, Utf8);
            _log.LogInfo($"[project] created AGENTS.md → {path}");
            return;
        }

        PatchAgentsFileIfNeeded(path);
    }

    private void PatchAgentsFileIfNeeded(string path)
    {
        var text = File.ReadAllText(path, Utf8);
        var patched = false;

        if (!text.Contains(MemorySeparationMarker, StringComparison.Ordinal))
        {
            text += MemorySeparationAppend;
            patched = true;
            _log.LogInfo($"[project] patched AGENTS.md (+memory separation) → {path}");
        }

        // Upgrade legacy AndroidChat / Android TTS sections → [Voice] protocol (AndroidChat ≥ 1.0.41).
        var hasLegacyAndroidSection =
            text.Contains(AndroidChatSectionMarker, StringComparison.Ordinal)
            || text.Contains(LegacyAndroidTtsSectionMarker, StringComparison.Ordinal)
            || text.Contains("[speak]", StringComparison.OrdinalIgnoreCase);

        if (hasLegacyAndroidSection
            && !text.Contains(VoiceProtocolMarker, StringComparison.OrdinalIgnoreCase))
        {
            var replaced = AndroidChatSectionRegex.Replace(text, AndroidChatSectionBody.TrimEnd() + "\n\n");
            if (!string.Equals(replaced, text, StringComparison.Ordinal))
            {
                text = replaced;
                patched = true;
                _log.LogInfo($"[project] patched AGENTS.md (+AndroidChat [Voice] protocol) → {path}");
            }
        }
        else if (!text.Contains(AndroidChatSectionMarker, StringComparison.Ordinal)
                 && !text.Contains(VoiceProtocolMarker, StringComparison.OrdinalIgnoreCase))
        {
            if (!text.Contains(ConciseReplyMarker, StringComparison.Ordinal))
            {
                text += ConciseAndTtsAppend;
                patched = true;
                _log.LogInfo($"[project] patched AGENTS.md (+concise / Android Voice TTS) → {path}");
            }
            else
            {
                text += "\n" + AndroidChatSectionBody;
                patched = true;
                _log.LogInfo($"[project] patched AGENTS.md (+AndroidChat [Voice] section) → {path}");
            }
        }
        else if (!text.Contains(ConciseReplyMarker, StringComparison.Ordinal))
        {
            text += ConciseAndTtsAppend;
            patched = true;
            _log.LogInfo($"[project] patched AGENTS.md (+concise / Android TTS) → {path}");
        }

        if (patched)
        {
            File.WriteAllText(path, text, Utf8);
        }
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content, Utf8);
    }

    private static void TryAppendGitignoreLine(string gitignorePath, string line)
    {
        if (File.Exists(gitignorePath))
        {
            var existing = File.ReadAllText(gitignorePath);
            if (existing.Contains(line, StringComparison.Ordinal))
            {
                return;
            }

            File.AppendAllText(gitignorePath, Environment.NewLine + line + Environment.NewLine, Utf8);
            return;
        }

        File.WriteAllText(gitignorePath, line + Environment.NewLine, Utf8);
    }
}
