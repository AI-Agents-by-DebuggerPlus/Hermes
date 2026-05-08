using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Static catalog of Hermes.Wpf + Hermes CLI capabilities for the Skills tab.</summary>
public static class AgentSkillsCatalog
{
    public static IReadOnlyList<AgentSkillCard> All { get; } =
    [
        new AgentSkillCard
        {
            Category = "Проект",
            Title = "Рабочая область и CLI Hermes",
            Summary =
                "Запуск Hermes в WSL с привязкой к выбранному проекту: статус, gateway, анализ кода и пользовательские запросы через терминал и чат.",
        },
        new AgentSkillCard
        {
            Category = "Память",
            Title = "External Brain (Markdown vault)",
            Summary =
                "Чтение локальных заметок Obsidian-стиля, поиск по тегам и тексту, подмешивание релевантных фрагментов в промпт при включённой опции в настройках.",
        },
        new AgentSkillCard
        {
            Category = "Синхронизация",
            Title = "Supabase relay",
            Summary =
                "Опрос таблицы messages и публикация ответов для синхронизации с Android и другими клиентами при заданных URL и anon key.",
        },
        new AgentSkillCard
        {
            Category = "Синхронизация",
            Title = "English Flashcards → WordPress",
            Summary =
                "При включённом relay приложение добавляет правила промпта: пользователь задаёт интервал карточек по теме, Hermes возвращает JSON skill flashcard_start/stop; " +
                "Hermes.Wpf ставит таймер, вызывает CLI для генерации пар en/ru и публикует JSON {\"type\":\"flashcard\",…} в messages для плагина WordPress.",
        },
        new AgentSkillCard
        {
            Category = "Обучение",
            Title = "Репетитор английского",
            Summary =
                "Фразами вроде «Будем учить английский» включается режим Hermes.Wpf: в статусной строке отображается индикатор, в промпт добавляются инструкции репетитора (размещение — 5 вопросов, лексика, прогресс). " +
                @"Модель в конце ответа выводит JSON между маркерами HERMES_TUTOR_SESSION_BEGIN … END (без тройных бэктиков — так безопаснее для Hermes CLI); приложение сохраняет слова локально и может экспортировать в Obsidian. Выкл.: «Закончим учить английский» и см. документацию.",
        },
        new AgentSkillCard
        {
            Category = "Чат",
            Title = "История и лог переписки",
            Summary =
                "Сохранение истории чата по проекту на диск, восстановление при смене проекта, отдельное окно чата с настраиваемым размером шрифта.",
        },
        new AgentSkillCard
        {
            Category = "Инструменты",
            Title = "Быстрые действия",
            Summary =
                "Кнопки Status, Gateway Run, Reset Webhook и сценарии через Hermes CLI без ручного набора команд в терминале.",
        },
        new AgentSkillCard
        {
            Category = "Desktop",
            Title = "Управление курсором мыши",
            Summary =
                "Win32: перемещение по экрану (виртуальный монитор), левый и правый клик. Включите в Settings навык курсора и используйте проверку на этой вкладке. Для вызовов из Hermes/WSL доступен Hermes.MouseBridge (после сборки копируется в папку Hermes.Wpf). Клавиатура и UI Automation — отдельным этапом (см. Docs/Plans).",
        },
    ];
}
