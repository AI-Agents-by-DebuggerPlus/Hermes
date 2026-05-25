using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public static class SettingsSaveFeedback
{
    public static string OpenRouterSectionHint(HermesSettings settings, bool persisted)
    {
        var model = string.IsNullOrWhiteSpace(settings.InAppAssistantOpenRouterModel)
            ? "openrouter/free"
            : settings.InAppAssistantOpenRouterModel.Trim();

        if (string.IsNullOrWhiteSpace(settings.InAppAssistantOpenRouterApiKey))
        {
            return persisted
                ? "Сохранено: OpenRouter API key не задан — ИИ-помощник (✦) не сможет отвечать, пока ключ не будет указан."
                : "OpenRouter API key пуст — получите ключ на openrouter.ai/keys. Для бесплатных моделей укажите openrouter/free или model:free.";
        }

        var tail = persisted ? " Сохранено." : " Нажмите «Сохранить», чтобы записать на диск.";
        return $"OpenRouter: ключ {MaskSecret(settings.InAppAssistantOpenRouterApiKey)}, модель {model}.{tail}";
    }

    public static string FullSettingsSaved(string settingsFilePath, HermesSettings settings)
    {
        var openRouter = string.IsNullOrWhiteSpace(settings.InAppAssistantOpenRouterApiKey)
            ? "OpenRouter key: не задан"
            : $"OpenRouter key: {MaskSecret(settings.InAppAssistantOpenRouterApiKey)}";
        var supabase = settings.SupabaseRelayEnabled && !string.IsNullOrWhiteSpace(settings.SupabaseUrl)
            ? "Supabase relay: включён"
            : "Supabase relay: выкл. или URL пуст";
        return $"Настройки сохранены в {settingsFilePath}. {openRouter}; {supabase}.";
    }

    public static string SupabaseSectionHint(HermesSettings settings) =>
        settings.SupabaseRelayEnabled
            ? string.IsNullOrWhiteSpace(settings.SupabaseUrl) || string.IsNullOrWhiteSpace(settings.SupabaseAnonKey)
                ? "Supabase relay включён, но URL или anon key пуст — relay не подключится."
                : $"Supabase relay включён ({settings.SupabaseUrl.Trim()})."
            : "Supabase relay выключен — чат с Android/DesktopVoiceChat не синхронизируется.";

    public static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(пусто)";
        }

        var t = value.Trim();
        return t.Length <= 8 ? "••••••••" : $"{t[..4]}…{t[^4..]}";
    }
}
