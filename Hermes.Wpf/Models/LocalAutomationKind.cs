namespace Hermes.Wpf.Models;

/// <summary>Built-in local automations invoked by Hermes CLI (wpf_local) with post-execution learning loop.</summary>
public enum LocalAutomationKind
{
    ReniWaterSubmit,
    ReniWaterAck,
    ReniWaterSchedule,
    ReniWaterSessionCheck,
}
