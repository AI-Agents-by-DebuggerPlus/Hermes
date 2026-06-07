namespace Hermes.Wpf.Services;



/// <summary>Outbound prompt: CLI-first local Windows actions via wpf_local JSON (post hermes chat).</summary>

public static class WpfLocalInstructions

{

    public const string OutboundBlockRu =

        "### WPF local tools (CLI-first — skill=wpf_local)\n"

        + "Hermes.Wpf **не выполняет задачи автonomously** (нет in-app таймеров, нет Task Scheduler в обход CLI). "

        + "Ты (Hermes CLI) решаешь задачу, используя навыки из `~/.hermes/skills/`, и при необходимости Windows-интеграции "

        + "возвращаешь JSON **`wpf_local`** или вызываешь `schtasks`/terminal **сам** (чтобы запомнить использованные инструменты).\n"

        + "Клиент выполнит wpf_local **после** твоего ответа и отправит post-hook обратно в CLI.\n\n"

        + "**Формат:** `{\"skill\":\"wpf_local\",\"action\":\"<action>\", ...}`\n\n"

        + "#### Reni Water (skill `builtin_reni_water`)\n"

        + "| action | когда |\n"

        + "|---|---|\n"

        + "| `reni_water_submit` | передать показания сейчас |\n"

        + "| `reni_water_ack` | подтвердить pending_ack |\n"

        + "| `reni_water_login` | вход на сайт |\n"

        + "| `reni_water_check_session` | проверить сессию |\n"

        + "| `reni_water_status` | статус + schtasks |\n"

        + "| `reni_water_schtasks_register` | зарегистрировать Windows Task Scheduler (только по твоему JSON) |\n"

        + "| `reni_water_schtasks_unregister` | удалить задачи schtasks |\n"

        + "| `reni_water_schedule` | legacy → маппится на schtasks register/unregister/status |\n\n"

        + "**Расписание:** предпочитай `schtasks /Create` через свой terminal tool; альтернатива — wpf_local schtasks_register. "

        + "Hermes.Wpf **никогда** не запускает передачу по расписанию сам.\n";

}

