namespace Hermes.Wpf.Services;

/// <summary>Outbound Hermes instructions for crystallizing reusable skills into Hermes.Wpf storage.</summary>
public static class SkillGenerationInstructions
{
    public const string OutboundBlockRu =
        "### Skill generation (Hermes.Wpf)\n"
        + "Когда пользователь просит **сохранить решение как навык**, **закристаллизовать skill**, или задача явно переиспользуема — "
        + "ответь **только** одним компактным JSON (без Markdown до/после):\n"
        + "{\"skill\":\"skill_save\",\"id\":\"<snake_case_id>\",\"title\":\"<краткое имя>\",\"summary\":\"<1–2 предложения>\","
        + "\"triggers\":[\"<фраза1>\",\"<фраза2>\"],\"kind\":\"script|prompt|intent\","
        + "\"script_body\":\"<полный текст run.ps1 или run.py; для kind=prompt оставь пустым>\","
        + "\"script_extension\":\"ps1|py\",\"outbound_prompt_block\":\"<инструкция для будущих чатов; опционально>\","
        + "\"test_command\":\"<команда smoke-теста; опционально>\"}\n"
        + "Правила: id — латиница, цифры, подчёркивание (3–48 символов). kind=script — исполняемый файл; kind=prompt — только блок в промпт; "
        + "kind=intent — запуск через JSON {\"skill\":\"wpf_local\",\"action\":\"…\"} (Windows tools внутри skill) или {\"skill\":\"run_generated\",\"id\":\"…\"}.\n"
        + "Для Windows-автоматизации (Reni Water и др.) — wpf_local, не run_generated.\n"
        + "Если сообщение не про сохранение/запуск навыка — не выводи этот JSON.";
}
