namespace DesktopVoiceChat.Models;

public sealed class AppSettings
{
    public string SupabaseUrl { get; set; } = string.Empty;

    public string SupabaseAnonKey { get; set; } = string.Empty;

    public string SenderName { get; set; } = "WPF User";

    /// <summary>
    /// После Connect вызывается GoTrue SignInAnonymously — отдельная «учётка» без email/пароля (включите Anonymous в Supabase).
    /// </summary>
    public bool UseAnonymousSession { get; set; } = true;

    /// <summary>API key OpenAI (хранится локально в settings.json).</summary>
    public string OpenAiApiKey { get; set; } = string.Empty;

    /// <summary>Например gpt-4o-mini.</summary>
    public string OpenAiModel { get; set; } = "gpt-4o-mini";

    /// <summary>Имя бота для сообщений-ответов в таблице messages.</summary>
    public string OpenAiBotSenderName { get; set; } = "Assistant";

    /// <summary>После вашей отправки запросить ответ OpenAI и записать её в чат через Supabase.</summary>
    public bool EnableOpenAiReplies { get; set; }
}
