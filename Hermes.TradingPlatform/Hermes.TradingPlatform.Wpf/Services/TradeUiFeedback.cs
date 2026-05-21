using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Wpf.Services;

/// <summary>Inline trade feedback (no modal dialogs).</summary>
public sealed class TradeUiFeedback
{
    public static TradeUiFeedback Instance { get; } = new();

    private string _lastMessage = string.Empty;

    public string LastMessage
    {
        get => _lastMessage;
        private set
        {
            if (_lastMessage == value)
            {
                return;
            }

            _lastMessage = value;
            MessageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? MessageChanged;

    public void ReportOrder(Order order, string context)
    {
        var ro = order.ReduceOnly ? " · reduce-only" : string.Empty;
        var prefix = order.Status == OrderStatus.Rejected ? "⚠ " : "✓ ";
        LastMessage =
            $"{prefix}{context}: {order.Id} {order.Status} — {order.Symbol} {order.Side} {order.Type} qty {order.Quantity}{ro}";
    }

    public void ReportWarning(string message) => LastMessage = $"⚠ {message}";

    public void ReportInfo(string message) => LastMessage = message;
}
