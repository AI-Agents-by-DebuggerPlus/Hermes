using DesktopVoiceChat.Models;
using Supabase;
using Supabase.Gotrue.Exceptions;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.PostgresChanges;
using static Supabase.Postgrest.Constants;

namespace DesktopVoiceChat.Services;

public class SupabaseChatService
{
    private Client? _client;
    private IRealtimeChannel? _messagesRealtimeChannel;
    private IRealtimeChannel.PostgresChangesHandler? _messagesInsertHandler;

    public bool IsConnected => _client is not null;

    /// <summary>Событие при INSERT в <c>public.messages</c> (Supabase Realtime).</summary>
    public event Action<Message>? MessageInserted;

    public async Task ConnectAsync(string url, string anonKey)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(anonKey))
        {
            throw new InvalidOperationException("Supabase URL and anon key are required.");
        }

        StopRealtimeMessagesSubscription();

        var options = new SupabaseOptions { AutoConnectRealtime = true };
        _client = new Client(url, anonKey, options);
        await _client.InitializeAsync();
    }

    /// <summary>Подписка на новые строки в <c>messages</c>. Вызывать после успешной аутентификации.</summary>
    public async Task StartRealtimeMessagesSubscriptionAsync()
    {
        EnsureConnected();
        StopRealtimeMessagesSubscription();

        _messagesInsertHandler = OnRealtimeMessageInsert;
        _messagesRealtimeChannel = await _client!
            .From<Message>()
            .On(PostgresChangesOptions.ListenType.Inserts, _messagesInsertHandler);
    }

    public void StopRealtimeMessagesSubscription()
    {
        if (_messagesRealtimeChannel is not null)
        {
            try
            {
                if (_messagesInsertHandler is not null)
                {
                    _messagesRealtimeChannel.RemovePostgresChangeHandler(
                        PostgresChangesOptions.ListenType.Inserts,
                        _messagesInsertHandler);
                }

                _messagesRealtimeChannel.Unsubscribe();
            }
            catch (Exception ex)
            {
                AppLogService.Log($"Realtime unsubscribe: {ex.Message}", "Chat");
            }
        }

        _messagesRealtimeChannel = null;
        _messagesInsertHandler = null;
    }

    private void OnRealtimeMessageInsert(IRealtimeChannel sender, PostgresChangesResponse change)
    {
        try
        {
            var model = change.Model<Message>();
            if (model is null)
            {
                return;
            }

            MessageInserted?.Invoke(model);
        }
        catch (Exception ex)
        {
            AppLogService.Log($"Realtime INSERT parse: {ex}", "Error");
        }
    }

    /// <summary>
    /// Создаёт сессию без email/пароля (провайдер Anonymous в Supabase Dashboard → Authentication).
    /// Анонимные пользователи получают роль <c>authenticated</c> в JWT; отличие от обычных — claim <c>is_anonymous</c>
    /// (см. <see href="https://supabase.com/docs/guides/auth/auth-anonymous">документацию</see>).
    /// </summary>
    public async Task EnsureAnonymousSessionAsync()
    {
        EnsureConnected();
        var existing = _client!.Auth.CurrentUser;
        if (existing is { Id: { Length: > 0 } })
        {
            return;
        }

        try
        {
            await _client.Auth.SignInAnonymously();
        }
        catch (GotrueException ex)
        {
            var safeDetail = string.IsNullOrWhiteSpace(ex.Content)
                ? ex.Message
                : $"{ex.Message} HTTP {(int?)ex.StatusCode}";
            AppLogService.Log($"Anonymous sign-in (GoTrue): {safeDetail}", "Auth");
            throw new InvalidOperationException(
                "Анонимный вход отклонён Supabase. Проверьте: Authentication → Providers → Anonymous включён; " +
                "если в проекте включена CAPTCHA для sign-up, без токена запрос будет отклонён " +
                "(см. https://supabase.com/docs/guides/auth/auth-captcha ); " +
                "лимит анонимных sign-up ~30 запросов/час с одного IP " +
                "(https://supabase.com/docs/guides/auth/auth-anonymous ). " +
                $"Ответ: {ex.Message}",
                ex);
        }

        var user = _client.Auth.CurrentUser;
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            throw new InvalidOperationException(
                "После анонимного входа сессия без user id. Проверьте URL, anon key и провайдер Anonymous.");
        }
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync()
    {
        EnsureConnected();
        var response = await _client!.From<Message>().Get();
        return response.Models
            .OrderBy(m => MessageChronologicalOrder.UtcTicksKey(m.CreatedAt))
            .ThenBy(m => m.Id)
            .ToList();
    }

    /// <param name="clientCreatedAt">Системное время клиента (локальная зона + смещение), сохраняется в <c>created_at</c> на сервере.</param>
    /// <returns>Строка с сервера (в т.ч. <c>created_at</c>); если PostgREST не вернул тело — <c>null</c> (тогда ждём Realtime).</returns>
    public async Task<Message?> SendMessageAsync(
        string senderName,
        string content,
        DateTimeOffset clientCreatedAt)
    {
        EnsureConnected();

        var currentUser = _client!.Auth.CurrentUser;
        if (currentUser is null)
        {
            throw new InvalidOperationException(
                "Current user is null. Configure auth first (Step 4) or use a signed-in session.");
        }

        var userId = currentUser.Id ?? throw new InvalidOperationException("Current user id is null.");

        var response = await _client.From<Message>().Insert(new Message
        {
            SenderId = userId,
            SenderName = senderName,
            Content = content,
            CreatedAt = clientCreatedAt,
        });

        if (response.Models is { Count: > 0 })
        {
            var row = response.Models[0];
            if (row.CreatedAt != default)
            {
                var deltaSec = Math.Abs((row.CreatedAt.ToUniversalTime() - clientCreatedAt.ToUniversalTime())
                    .TotalSeconds);
                if (deltaSec > 5)
                {
                    AppLogService.Log(
                        $"PostgREST: created_at в ответе сильно отличается от отправленного (Δ≈{deltaSec:0} с). " +
                        "Частая причина — триггер BEFORE INSERT, подменяющий created_at (см. Docs/Supabase/003 и 004). " +
                        $"Отправлено: {clientCreatedAt:O}, в ответе: {row.CreatedAt:O}.",
                        "Chat");
                }
            }

            return row;
        }

        return response.Model;
    }

    /// <summary>
    /// Удаляет все строки из <c>messages</c> через PostgREST (<c>id=in.(...)</c>).
    /// Требуется политика RLS DELETE для роли authenticated (см. <c>Docs/Supabase/001_messages_table.sql</c>
    /// или <c>002_messages_delete_policy.sql</c>). После успешной серии удалений выполняется повторное чтение;
    /// если строки ещё есть — выбросится исключение (частые причины: нет политики DELETE; неверный фильтр клиента).
    /// </summary>
    public async Task ClearAllMessagesAsync()
    {
        EnsureConnected();

        var list = (await GetMessagesAsync()).OrderByDescending(m => m.CreatedAt).ToList();
        if (list.Count == 0)
        {
            return;
        }

        foreach (Guid[] chunk in list.Select(m => m.Id).Chunk(100))
        {
            var idOperands = chunk.Cast<object>().ToList();
            await _client!
                .From<Message>()
                .Filter("id", Operator.In, idOperands)
                .Delete();
        }

        var remaining = await GetMessagesAsync();
        if (remaining.Count > 0)
        {
            throw new InvalidOperationException(
                $"Не удалены все сообщения на сервере (осталось {remaining.Count}). " +
                "Выполните SQL политики DELETE из Docs/Supabase/002_messages_delete_policy.sql.");
        }
    }

    private void EnsureConnected()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Supabase client is not connected.");
        }
    }
}
