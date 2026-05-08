using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using DesktopVoiceChat.Models;
using DesktopVoiceChat.Services;

namespace DesktopVoiceChat.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly SupabaseChatService _chatService = new();
    private readonly OpenAiReplyService _openAi = new();
    private string _supabaseUrl = string.Empty;
    private string _supabaseAnonKey = string.Empty;
    private string _senderName = "WPF User";
    private string _draftMessage = string.Empty;
    private string _status = "Ready";
    private bool _useAnonymousSession = true;
    private string _openAiApiKey = string.Empty;
    private string _openAiModel = "gpt-4o-mini";
    private string _openAiBotSenderName = "Assistant";
    private bool _enableOpenAiReplies;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Message> Messages { get; } = [];

    public MainViewModel()
    {
        var loaded = AppSettingsStore.Load();
        SupabaseUrl = loaded.SupabaseUrl;
        SupabaseAnonKey = loaded.SupabaseAnonKey;
        SenderName = string.IsNullOrWhiteSpace(loaded.SenderName) ? "WPF User" : loaded.SenderName;
        UseAnonymousSession = loaded.UseAnonymousSession;
        OpenAiApiKey = loaded.OpenAiApiKey ?? string.Empty;
        OpenAiModel = string.IsNullOrWhiteSpace(loaded.OpenAiModel) ? "gpt-4o-mini" : loaded.OpenAiModel.Trim();
        OpenAiBotSenderName = string.IsNullOrWhiteSpace(loaded.OpenAiBotSenderName)
            ? "Assistant"
            : loaded.OpenAiBotSenderName.Trim();
        EnableOpenAiReplies = loaded.EnableOpenAiReplies;
        if (File.Exists(AppSettingsStore.FilePath))
        {
            AppLogService.Log("Настройки загружены из локального файла.", "Settings");
        }
        else
        {
            AppLogService.Log("Файл настроек не найден — поля пустые. Задайте ключи в «Настройки».", "Settings");
        }
        AppLogService.Log($"Начальный статус: {_status}", "Status");
        _chatService.MessageInserted += OnRealtimeMessageInserted;
    }

    public string SupabaseUrl
    {
        get => _supabaseUrl;
        set => SetField(ref _supabaseUrl, value);
    }

    public string SupabaseAnonKey
    {
        get => _supabaseAnonKey;
        set => SetField(ref _supabaseAnonKey, value);
    }

    public string SenderName
    {
        get => _senderName;
        set => SetField(ref _senderName, value);
    }

    public string DraftMessage
    {
        get => _draftMessage;
        set => SetField(ref _draftMessage, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool UseAnonymousSession
    {
        get => _useAnonymousSession;
        set => SetField(ref _useAnonymousSession, value);
    }

    public string OpenAiApiKey
    {
        get => _openAiApiKey;
        set => SetField(ref _openAiApiKey, value ?? string.Empty);
    }

    public string OpenAiModel
    {
        get => _openAiModel;
        set => SetField(ref _openAiModel, string.IsNullOrWhiteSpace(value) ? "gpt-4o-mini" : value.Trim());
    }

    public string OpenAiBotSenderName
    {
        get => _openAiBotSenderName;
        set =>
            SetField(ref _openAiBotSenderName, string.IsNullOrWhiteSpace(value) ? "Assistant" : value.Trim());
    }

    public bool EnableOpenAiReplies
    {
        get => _enableOpenAiReplies;
        set => SetField(ref _enableOpenAiReplies, value);
    }

    /// <summary>
    /// Подключение при старте приложения, если в настройках заданы URL и anon key.
    /// </summary>
    public async Task ConnectIfConfiguredAsync()
    {
        if (string.IsNullOrWhiteSpace(SupabaseUrl) || string.IsNullOrWhiteSpace(SupabaseAnonKey))
        {
            AppLogService.Log("Автоподключение пропущено: задайте Supabase URL и anon key в «Настройки».", "Settings");
            return;
        }

        AppLogService.Log("Автоподключение к Supabase…", "Status");
        await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        try
        {
            ReportStatus("Connecting to Supabase...");
            await _chatService.ConnectAsync(SupabaseUrl, SupabaseAnonKey);
            if (UseAnonymousSession)
            {
                ReportStatus("Анонимный вход…");
                await _chatService.EnsureAnonymousSessionAsync();
                AppLogService.Log("Анонимная сессия Supabase установлена (без email/пароля).", "Auth");
            }

            ReportStatus("Подписка Realtime на messages…");
            await _chatService.StartRealtimeMessagesSubscriptionAsync();
            AppLogService.Log("Realtime: слушаем INSERT в public.messages.", "Chat");

            await RefreshAsync();
            ReportStatus("Connected");
        }
        catch (Exception ex)
        {
            ReportStatus($"Connect failed: {ex.Message}");
            AppLogService.Log($"Connect failed: {ex}", "Error");
        }
    }

    /// <returns><c>false</c> если не подключено или запрос упал.</returns>
    public async Task<bool> RefreshAsync()
    {
        if (!_chatService.IsConnected)
        {
            ReportStatus("Connect to Supabase first.");
            return false;
        }

        try
        {
            AppLogService.Log("Refresh: загрузка сообщений.", "Chat");
            var items = await _chatService.GetMessagesAsync();
            Messages.Clear();
            foreach (var message in items)
            {
                AndroidChatLogCapture.TryAppendFromChatMessage(message);
                Messages.Add(message);
            }

            ReportStatus($"Loaded {Messages.Count} messages.");
            return true;
        }
        catch (Exception ex)
        {
            ReportStatus($"Refresh failed: {ex.Message}");
            AppLogService.Log($"Refresh failed: {ex}", "Error");
            return false;
        }
    }


    public async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(DraftMessage))
        {
            return;
        }

        if (!_chatService.IsConnected)
        {
            ReportStatus("Connect to Supabase first.");
            return;
        }

        try
        {
            ReportStatus("Sending message...");
            var preview = DraftMessage.Trim();
            var clientCreatedAt = DateTimeOffset.Now;
            AppLogService.Log($"Send: длина текста {preview.Length} символов.", "Chat");
            var savedUser = await _chatService.SendMessageAsync(SenderName, preview, clientCreatedAt);
            DraftMessage = string.Empty;
            if (savedUser is not null)
            {
                EnsureUi(() => UpsertChatMessage(savedUser));
            }
            else
            {
                AppLogService.Log("Send: PostgREST не вернул строку — ждём Realtime для created_at.", "Chat");
            }

            if (EnableOpenAiReplies && !string.IsNullOrWhiteSpace(OpenAiApiKey))
            {
                ReportStatus("OpenAI: запрос ответа…");
                AppLogService.Log("OpenAI: Chat Completions…", "Chat");
                try
                {
                    var reply = await _openAi.GetReplyAsync(OpenAiApiKey, OpenAiModel, preview).ConfigureAwait(true);
                    if (!string.IsNullOrWhiteSpace(reply))
                    {
                        var savedBot = await _chatService.SendMessageAsync(
                            OpenAiBotSenderName,
                            reply,
                            DateTimeOffset.Now);
                        if (savedBot is not null)
                        {
                            EnsureUi(() => UpsertChatMessage(savedBot));
                        }
                    }

                    ReportStatus("Сообщение отправлено, ответ бота записан.");
                }
                catch (Exception oex)
                {
                    ReportStatus($"OpenAI: {oex.Message}");
                    AppLogService.Log($"OpenAI failed: {oex}", "Error");
                }
            }
            else
            {
                ReportStatus("Message sent.");
            }
        }
        catch (Exception ex)
        {
            ReportStatus($"Send failed: {ex.Message}");
            AppLogService.Log($"Send failed: {ex}", "Error");
        }
    }

    public async Task ClearChatAsync()
    {
        if (!_chatService.IsConnected)
        {
            ReportStatus("Connect to Supabase first.");
            return;
        }

        try
        {
            ReportStatus("Очистка чата на сервере…");
            AppLogService.Log("Очистка чата: удаление всех сообщений.", "Chat");
            await _chatService.ClearAllMessagesAsync();
            if (!await RefreshAsync())
            {
                return;
            }

            ReportStatus(Messages.Count == 0
                ? "Чат очищен (сервер)."
                : $"На сервере осталось {Messages.Count} сообщ.");
        }
        catch (Exception ex)
        {
            ReportStatus($"Очистка чата не удалась: {ex.Message}");
            AppLogService.Log($"Clear chat failed: {ex}", "Error");
        }
    }

    private void OnRealtimeMessageInserted(Message message)
    {
        void Apply()
        {
            UpsertChatMessage(message);
            AppLogService.Log($"Realtime: сообщение {message.Id}.", "Chat");
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.BeginInvoke(Apply);
        }
    }

    /// <summary>
    /// Обновляет или вставляет сообщение. При дубликате по <c>id</c> (PostgREST + Realtime) не подменяем <c>created_at</c>
    /// локальным временем — берём входящую строку с сервера, чтобы UI совпадал с БД.
    /// </summary>
    private void UpsertChatMessage(Message incoming)
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].Id != incoming.Id)
            {
                continue;
            }

            incoming = PreferMessageWithTimestamp(Messages[i], incoming);
            Messages.RemoveAt(i);
            break;
        }

        var idx = MessageChronologicalOrder.InsertIndex(Messages, incoming);
        Messages.Insert(idx, incoming);
        AndroidChatLogCapture.TryAppendFromChatMessage(incoming);
    }

    private static Message PreferMessageWithTimestamp(Message a, Message b)
    {
        var aOk = a.CreatedAt != default;
        var bOk = b.CreatedAt != default;
        if (aOk && !bOk)
        {
            return a;
        }

        if (!aOk && bOk)
        {
            return b;
        }

        if (aOk && bOk)
        {
            // Одинаковый момент времени (в т.ч. разный Kind) — оставляем уже показанное.
            if (MessageChronologicalOrder.UtcTicksKey(a.CreatedAt) ==
                MessageChronologicalOrder.UtcTicksKey(b.CreatedAt))
            {
                return a;
            }

            // Иначе приоритет у последнего события с сервера (Realtime или ответ Insert), не у локального UtcNow.
            return b;
        }

        return b;
    }

    private static void EnsureUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private void ReportStatus(string value)
    {
        Status = value;
        AppLogService.Log(value, "Status");
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
