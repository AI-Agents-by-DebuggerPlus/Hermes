namespace Hermes.Wpf.Models;

/// <summary>WhatsApp Web monitor readiness for chat UI indicator.</summary>
public enum WhatsAppMonitorReadiness
{
    Off,
    Starting,
    QrRequired,
    OpeningChat,
    Baseline,
    Probing,
    Ready,
    Stalled,
    Error,
}
