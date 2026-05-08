namespace Hermes.Wpf.Skills;

public interface IFlashcardSkill : IDisposable
{
    FlashcardStatus Status { get; }

    /// <summary>When <see cref="Status"/> is <see cref="FlashcardStatus.WaitingToStart"/>, UTC instant when generating should begin.</summary>
    DateTimeOffset? ScheduledGenerationUtc { get; }

    void Start(string topic, int intervalMinutes, int delayMinutes);

    void Stop();

    event EventHandler<FlashcardStatus>? StatusChanged;

    /// <summary>Raised periodically while waiting so the UI can refresh remaining delay text.</summary>
    event EventHandler? DelayTick;
}
