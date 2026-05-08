namespace Hermes.Wpf.Models;

/// <summary>Built-in outbound instructions (not shown in UI) to reduce contradictory claims and context drift.</summary>
public static class ChatBehaviorDefaults
{
    /// <summary>Instruction precedence used for every outbound chat message.</summary>
    public const string InstructionPriorityRu =
        "Приоритет инструкций: прямые текущие указания пользователя в этом чате важнее текста из документации/README/инструкций проекта. " +
        "Документацию трактуй как контекст по умолчанию и не используй её для отмены явной команды пользователя, если команда безопасна и технически выполнима.";

    /// <summary>Keep file/path operations exact to user wording.</summary>
    public const string TaskPrecisionRu =
        "Выполняй задачу точно по формулировке пользователя: не меняй целевую папку/файл/путь без явного запроса. " +
        "Если путь неоднозначен — сначала уточни, а не делай предположение.";

    /// <summary>Appended only to payloads sent to <c>hermes chat</c>, not to the transcript file displayed to the user.</summary>
    public const string VisionScopeReminderRu =
        "Не утверждай, что видишь рабочий стол, окна или экран пользователя, если в этом сообщении ты не выполнил успешный захват изображения " +
        "(скриншот, vision_analyze с реальным файлом/URL и без ошибок). " +
        "Если опираешься только на текст чата — прямо скажи, что видишь только текст и не можешь видеть экран. " +
        "Не выдумывай детали интерфейса без подтверждённого визуального входа.";

    /// <summary>When user sends a ping-style numeric code (relay/Supabase tests), model must repeat it verbatim.</summary>
    public const string VerificationCodeEchoRu =
        "Проверочный код: если пользователь сообщил числовой код для проверки (в т.ч. тест ping/relay) или текст состоит в основном из такого кода, " +
        "ответь коротко и **обязательно включи в ответ те же цифры дословно** (ничего не опускать и не переписывать число). Это нужно подтвердить доставку сообщения.";

    /// <summary>Always inform the model about client-only «skills» (not part of upstream Hermes docs).</summary>
    public const string HermesWpfClientCapabilitiesRu =
        "Клиент **Hermes.Wpf** (Windows, не путать с CLI): навык **репетитора английского** управляется фразами пользователя в чате: " +
        "включение — например «Будем учить английский», «режим репетитора»; выключение — «Закончим учить английский», «общий режим», «выключи репетитор». " +
        "Клиент добавляет блок инструкций репетитора в промпт, пока режим активен (placement 5 коротких вопросов, лексика, прогресс). Если пользователь просит репетитора — не отвечай, что этого «нет в описании навыков CLI».";
}
