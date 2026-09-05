using System.Collections.ObjectModel;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>In-memory ring buffer of agent activity for Mini Console / MainConsole.</summary>
public sealed class AgentActivityBus
{
    private readonly object _gate = new();
    private readonly List<AgentActivityEvent> _events = [];
    private const int MaxEvents = 2000;

    public event Action? Changed;

    public void Publish(string workspace, string kind, string message, bool isError = false)
    {
        var ev = new AgentActivityEvent
        {
            Workspace = string.IsNullOrWhiteSpace(workspace) ? "(none)" : workspace.Trim(),
            Kind = string.IsNullOrWhiteSpace(kind) ? "info" : kind.Trim(),
            Message = (message ?? string.Empty).Trim(),
            IsError = isError,
            AtLocal = DateTime.Now,
        };

        lock (_gate)
        {
            _events.Add(ev);
            if (_events.Count > MaxEvents)
            {
                _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<AgentActivityEvent> Snapshot(string? workspaceFilter = null, int takeLast = 500)
    {
        lock (_gate)
        {
            IEnumerable<AgentActivityEvent> q = _events;
            if (!string.IsNullOrWhiteSpace(workspaceFilter))
            {
                q = q.Where(e =>
                    string.Equals(e.Workspace, workspaceFilter, StringComparison.OrdinalIgnoreCase));
            }

            return q.TakeLast(Math.Clamp(takeLast, 50, MaxEvents)).ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }

        Changed?.Invoke();
    }
}
