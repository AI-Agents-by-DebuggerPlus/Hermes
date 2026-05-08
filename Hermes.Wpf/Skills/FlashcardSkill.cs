using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Skills;

/// <summary>Posts JSON flashcards on a timer into Supabase for the WordPress English Flashcards plugin.</summary>
public sealed class FlashcardSkill : IFlashcardSkill
{
    public delegate Task<string?> GenerateCardJsonAsync(
        string topic,
        IReadOnlyList<string> englishAlreadySent,
        bool retryStricterPrompt,
        CancellationToken cancellationToken);

    public delegate Task<bool> PublishJsonAsync(string json, CancellationToken cancellationToken);

    private readonly LogService _log;
    private readonly GenerateCardJsonAsync _generate;
    private readonly PublishJsonAsync _publish;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _runner;
    private FlashcardStatus _status = FlashcardStatus.Idle;
    private string _topic = string.Empty;
    private int _intervalMinutes = 10;
    private readonly HashSet<string> _englishSent = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _scheduledGenerationUtc;

    /// <inheritdoc />
    public FlashcardStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public string CurrentTopic
    {
        get
        {
            lock (_gate)
            {
                return _topic;
            }
        }
    }

    public int CurrentIntervalMinutes
    {
        get
        {
            lock (_gate)
            {
                return _intervalMinutes;
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? ScheduledGenerationUtc
    {
        get
        {
            lock (_gate)
            {
                return _scheduledGenerationUtc;
            }
        }
    }

    public event EventHandler<FlashcardStatus>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler? DelayTick;

    public FlashcardSkill(LogService log, GenerateCardJsonAsync generate, PublishJsonAsync publish)
    {
        _log = log;
        _generate = generate;
        _publish = publish;
    }

    /// <inheritdoc />
    public void Start(string topic, int intervalMinutes, int delayMinutes)
    {
        topic = (topic ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(topic))
        {
            topic = "mixed topics";
        }

        intervalMinutes = Math.Clamp(intervalMinutes, 1, 24 * 60);
        delayMinutes = Math.Clamp(delayMinutes, 0, 7 * 24 * 60);

        lock (_gate)
        {
            StopInternalLocked();
            _englishSent.Clear();
            _topic = topic;
            _intervalMinutes = intervalMinutes;
            _scheduledGenerationUtc = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
            _cts = new CancellationTokenSource();
            SetStatusUnsafe(FlashcardStatus.WaitingToStart);

            var delayCopy = delayMinutes;
            var intervalCopy = intervalMinutes;
            var token = _cts.Token;
            _runner = RunLoopAsync(delayCopy, intervalCopy, token);
        }

        RaiseDelayTickUnsafe();
        _log.LogInfo($"[flashcards] Start topic={topic} intervalMin={intervalMinutes} delayMin={delayMinutes}");
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            StopInternalLocked();
            SetStatusUnsafe(FlashcardStatus.Idle);
            _scheduledGenerationUtc = null;
        }

        _log.LogInfo("[flashcards] Stop()");
        RaiseStatusUnsafe(FlashcardStatus.Idle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
            _runner = null;
        }
    }

    private async Task RunLoopAsync(int delayMinutes, int intervalMinutes, CancellationToken token)
    {
        try
        {
            if (delayMinutes > 0)
            {
                var delayEnd = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
                while (!token.IsCancellationRequested && DateTimeOffset.UtcNow < delayEnd)
                {
                    if (DateTimeOffset.UtcNow.Second % 15 == 0)
                    {
                        var rem = delayEnd - DateTimeOffset.UtcNow;
                        _log.LogInfo($"[flashcards] waiting: remaining≈{Math.Max(0, (int)Math.Ceiling(rem.TotalSeconds))}s topic={CurrentTopic}");
                    }
                    RaiseDelayTickUnsafe();
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            lock (_gate)
            {
                _scheduledGenerationUtc = null;
                SetStatusUnsafe(FlashcardStatus.Generating);
            }

            _log.LogInfo($"[flashcards] status=Generating topic={CurrentTopic} intervalMin={CurrentIntervalMinutes}");
            RaiseDelayTickUnsafe();

            while (!token.IsCancellationRequested)
            {
                _log.LogInfo("[flashcards] tick: generate+publish");
                await TryGeneratePublishOnceAsync(token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // normal on Stop/dispose
        }
        finally
        {
            lock (_gate)
            {
                if (_status is FlashcardStatus.Generating or FlashcardStatus.WaitingToStart)
                {
                    SetStatusUnsafe(FlashcardStatus.Idle);
                }

                _scheduledGenerationUtc = null;
            }

            RaiseStatusUnsafe(FlashcardStatus.Idle);
            RaiseDelayTickUnsafe();
            _log.LogInfo("[flashcards] loop ended");
        }
    }

    private async Task TryGeneratePublishOnceAsync(CancellationToken token)
    {
        string topic;
        int already;
        lock (_gate)
        {
            topic = _topic;
            already = _englishSent.Count;
        }

        for (var retry = false;; retry = true)
        {
            string? raw;
            try
            {
                _log.LogInfo($"[flashcards] generating via Hermes (retry={retry}) topic={topic} sent={already}");
                raw = await _generate(topic, _englishSent.ToList(), retry, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarn($"[flashcards] Hermes generation exception: {ex.Message}");
                return;
            }

            _log.LogInfo($"[flashcards] Hermes returned chars={(raw ?? string.Empty).Length}");
            if (TryPickValidPayload(raw ?? string.Empty, out var envelope))
            {
                var json = SerializeEnvelope(envelope.En, envelope.Ru);
                bool ok;
                try
                {
                    ok = await _publish(json, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.LogWarn($"[flashcards] Supabase publish exception: {ex.Message}");
                    return;
                }

                if (ok)
                {
                    lock (_gate)
                    {
                        _englishSent.Add(envelope.En);
                    }

                    _log.LogInfo($"[flashcards] posted en=\"{envelope.En}\"");
                }

                return;
            }

            if (retry)
            {
                _log.LogWarn("[flashcards] Invalid JSON twice — skipping this tick.");
                return;
            }

            _log.LogWarn("[flashcards] Invalid JSON — retrying once with stricter prompt.");
        }
    }

    private static bool TryPickValidPayload(string combined, out (string En, string Ru) envelope)
    {
        envelope = default;
        foreach (var candidate in EnumerateJsonObjectCandidates(combined))
        {
            if (!TryDeserializeFlashcard(candidate, out var en, out var ru))
            {
                continue;
            }

            envelope = (en, ru);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateJsonObjectCandidates(string text)
    {
        var t = text.Trim();
        if (string.IsNullOrEmpty(t))
        {
            yield break;
        }

        yield return t;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }

            var depth = 0;
            for (var j = i; j < text.Length; j++)
            {
                var c = text[j];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text[i..(j + 1)];
                        break;
                    }
                }
            }
        }
    }

    private static bool TryDeserializeFlashcard(string json, out string en, out string ru)
    {
        en = string.Empty;
        ru = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl))
            {
                return false;
            }

            if (!string.Equals(typeEl.GetString(), "flashcard", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!root.TryGetProperty("en", out var enEl) || !root.TryGetProperty("ru", out var ruEl))
            {
                return false;
            }

            en = (enEl.GetString() ?? string.Empty).Trim();
            ru = (ruEl.GetString() ?? string.Empty).Trim();
            return en.Length > 0 && ru.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions FlashcardSupabaseJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string SerializeEnvelope(string en, string ru)
    {
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "flashcard",
            ["en"] = en,
            ["ru"] = ru,
        }, FlashcardSupabaseJsonOptions);
    }

    private void RaiseDelayTickUnsafe()
    {
        DelayTick?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseStatusUnsafe(FlashcardStatus st)
    {
        StatusChanged?.Invoke(this, st);
    }

    private void SetStatusUnsafe(FlashcardStatus st)
    {
        _status = st;
        StatusChanged?.Invoke(this, st);
    }

    private void StopInternalLocked()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _scheduledGenerationUtc = null;
        _runner = null;
    }
}
