using System.Media;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Wpf.Services;

/// <summary>MetaTrader-style trade sounds on fills (Windows system sounds).</summary>
public sealed class TradingSoundService : IDisposable
{
    private readonly Func<bool> _soundsEnabled;

    public TradingSoundService(IEventBus bus, Func<bool> soundsEnabled)
    {
        _soundsEnabled = soundsEnabled;
        bus.Subscribe<OrderFilledEvent>(OnOrderFilled);
    }

    private void OnOrderFilled(OrderFilledEvent filled)
    {
        if (!_soundsEnabled())
        {
            return;
        }

        try
        {
            if (filled.Order.Status == OrderStatus.Rejected)
            {
                SystemSounds.Hand.Play();
                return;
            }

            switch (filled.JournalKind)
            {
                case "Close":
                    SystemSounds.Asterisk.Play();
                    break;
                case "Reduce":
                    SystemSounds.Exclamation.Play();
                    break;
                case "Open":
                case "Add":
                    SystemSounds.Beep.Play();
                    break;
                default:
                    SystemSounds.Beep.Play();
                    break;
            }
        }
        catch
        {
            // ignore audio failures
        }
    }

    public void Dispose() { }
}
