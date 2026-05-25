using Hermes.InAppAssistant;

namespace Hermes.Wpf.Services;

/// <summary>Outbound Hermes instructions when Supabase relay feeds Android TTS.</summary>
public static class AndroidTtsSupabaseInstructions
{
    /// <summary>Append to <c>hermes chat</c> when Supabase relay is enabled.</summary>
    public static string OutboundBlockRu =>
        AppAssistantKnowledge.AndroidTtsSupabaseOutboundRu
        + "\n\nЕсли пользователь просит карточки (flashcard) — следуй инструкциям flashcard JSON выше; "
        + "для всех остальных вопросов — только JSON по одному предложению на строку, как в примере.";
}
