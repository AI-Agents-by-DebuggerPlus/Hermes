namespace Hermes.TradingPlatform.Strategies;

internal sealed class StrategyCooldown
{
    private TimeSpan _interval;
    private DateTimeOffset _lastSignalAt = DateTimeOffset.MinValue;

    public StrategyCooldown(TimeSpan interval) => _interval = interval;

    public TimeSpan Interval
    {
        get => _interval;
        set => _interval = value;
    }

    public bool TryAcquire()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSignalAt < _interval)
        {
            return false;
        }

        _lastSignalAt = now;
        return true;
    }
}
