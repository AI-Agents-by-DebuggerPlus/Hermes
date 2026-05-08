namespace Hermes.Wpf.Services;

/// <summary>Outbound Hermes instructions when Supabase relay feeds the WordPress «English Flashcards» plugin.</summary>
public static class FlashcardRelayInstructions
{
    /// <summary>Append to <c>hermes chat</c> payload (RU) so the assistant emits flashcard_start / flashcard_stop JSON when the user asks.</summary>
    public const string OutboundBlockRu =
        "### Supabase: English Flashcards (WordPress)\n"
        + "Плагин показывает последнюю строку в таблице messages с content = валидный JSON вида "
        + "{\"type\":\"flashcard\",\"en\":\"...\",\"ru\":\"...\"}. "
        + "Когда пользователь просит генерировать карточки или остановить их, ответь **только** компактным JSON (без Markdown, без текста до или после), строго одним из вариантов:\n"
        + "• Старт (распознай фразы про тему, интервал в минутах и задержку старта):\n"
        + "  {\"skill\":\"flashcard_start\",\"topic\":\"<тема англ>\",\"interval_minutes\":<число не меньше 1>,\"delay_minutes\":<число не меньше 0>}\n"
        + "• Стоп (слова: stop, хватит, стоп, закончить просмотр карточек):\n"
        + "  {\"skill\":\"flashcard_stop\"}\n"
        + "Если это обычное сообщение пользователя не про карточки — не выводи этот JSON; отвечай как обычно.";
}
