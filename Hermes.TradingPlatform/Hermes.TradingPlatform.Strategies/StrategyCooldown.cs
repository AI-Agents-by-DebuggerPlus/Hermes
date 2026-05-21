namespace Hermes.TradingPlatform.Strategies;

internal sealed class StrategyCooldown
{
    private readonly TimeSpan _interval;
    private DateTimeOffset _lastSignalAt = DateTimeOffset.MinValue;

    public StrategyCooldown(TimeSpan interval) => _interval = interval;

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
