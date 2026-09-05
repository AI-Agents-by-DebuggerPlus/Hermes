using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// In-app agent reminder scheduler. Stores tasks; at due time raises <see cref="TaskDue"/>
/// so MainViewModel can inject the command into the owning project's chat.
/// Does not run Playwright/schtasks itself.
/// </summary>
public sealed class AgentTaskSchedulerService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private readonly LogService _log;
    private readonly string _storePath;
    private readonly DispatcherTimer _timer;
    private List<AgentScheduledTask> _tasks = [];
    private bool _dispatching;

    public AgentTaskSchedulerService(LogService log)
    {
        _log = log;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf",
            "scheduler");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, "tasks.json");
        Load();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Tick();
    }

    public event Action<AgentScheduledTask>? TaskDue;

    public event Action? Changed;

    public string StorePath => _storePath;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public IReadOnlyList<AgentScheduledTask> GetAll()
    {
        lock (_gate)
        {
            return _tasks.OrderBy(t => t.DueAtLocal).ToList();
        }
    }

    public IReadOnlyList<AgentScheduledTask> GetActive()
    {
        lock (_gate)
        {
            return _tasks.Where(t => t.IsActive).OrderBy(t => t.DueAtLocal).ToList();
        }
    }

    public AgentScheduledTask Add(
        string title,
        string command,
        string projectName,
        DateTime dueAtLocal,
        string createdBy = "agent",
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("title required");
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("command required");
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("project required");
        }

        if (dueAtLocal <= DateTime.Now.AddMinutes(-1))
        {
            throw new ArgumentException("due time must be in the future (or within the last minute)");
        }

        var task = new AgentScheduledTask
        {
            Title = title.Trim(),
            Command = command.Trim(),
            ProjectName = projectName.Trim(),
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "agent" : createdBy.Trim(),
            DueAtLocal = dueAtLocal,
            Notes = notes?.Trim(),
            Status = AgentTaskStatus.Scheduled,
            CreatedAtLocal = DateTime.Now,
        };

        lock (_gate)
        {
            _tasks.Add(task);
            SaveUnlocked();
        }

        _log.LogInfo(
            $"[scheduler] add id={task.Id} project={task.ProjectName} due={task.DueAtLocal:O} «{task.Title}»");
        Changed?.Invoke();
        return task;
    }

    public bool TryComplete(string taskId, out AgentScheduledTask? task)
    {
        task = null;
        lock (_gate)
        {
            var t = FindUnlocked(taskId);
            if (t is null)
            {
                return false;
            }

            t.Status = AgentTaskStatus.Completed;
            t.CompletedAtLocal = DateTime.Now;
            task = t;
            SaveUnlocked();
        }

        _log.LogInfo($"[scheduler] complete id={taskId}");
        Changed?.Invoke();
        return true;
    }

    public bool TryCancel(string taskId, out AgentScheduledTask? task)
    {
        task = null;
        lock (_gate)
        {
            var t = FindUnlocked(taskId);
            if (t is null)
            {
                return false;
            }

            t.Status = AgentTaskStatus.Cancelled;
            t.CompletedAtLocal = DateTime.Now;
            task = t;
            SaveUnlocked();
        }

        _log.LogInfo($"[scheduler] cancel id={taskId}");
        Changed?.Invoke();
        return true;
    }

    public bool TryRemove(string taskId)
    {
        lock (_gate)
        {
            var n = _tasks.RemoveAll(t => string.Equals(t.Id, taskId, StringComparison.OrdinalIgnoreCase));
            if (n == 0)
            {
                return false;
            }

            SaveUnlocked();
        }

        _log.LogInfo($"[scheduler] remove id={taskId}");
        Changed?.Invoke();
        return true;
    }

    /// <summary>Mark as Fired and persist (called after command was dispatched to agent).</summary>
    public void MarkFired(string taskId)
    {
        lock (_gate)
        {
            var t = FindUnlocked(taskId);
            if (t is null)
            {
                return;
            }

            t.Status = AgentTaskStatus.Fired;
            t.FiredAtLocal = DateTime.Now;
            SaveUnlocked();
        }

        Changed?.Invoke();
    }

    /// <summary>Re-queue a Fired task for immediate dispatch (dashboard Run Now).</summary>
    public bool TryRequeueForRunNow(string taskId, out AgentScheduledTask? task)
    {
        task = null;
        lock (_gate)
        {
            var t = FindUnlocked(taskId);
            if (t is null || t.Status is AgentTaskStatus.Completed or AgentTaskStatus.Cancelled)
            {
                return false;
            }

            t.Status = AgentTaskStatus.Scheduled;
            t.DueAtLocal = DateTime.Now.AddSeconds(-1);
            t.FiredAtLocal = null;
            task = Clone(t);
            SaveUnlocked();
        }

        Changed?.Invoke();
        Tick();
        return true;
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void Tick()
    {
        if (_dispatching)
        {
            return;
        }

        List<AgentScheduledTask> due;
        lock (_gate)
        {
            var now = DateTime.Now;
            var ripe = _tasks
                .Where(t => t.Status == AgentTaskStatus.Scheduled && t.DueAtLocal <= now)
                .OrderBy(t => t.DueAtLocal)
                .ToList();
            if (ripe.Count == 0)
            {
                return;
            }

            // Mark Fired before invoke so the next tick cannot re-dispatch while agent runs.
            foreach (var t in ripe)
            {
                t.Status = AgentTaskStatus.Fired;
                t.FiredAtLocal = now;
            }

            SaveUnlocked();
            due = ripe.Select(Clone).ToList();
        }

        Changed?.Invoke();

        _dispatching = true;
        try
        {
            foreach (var task in due)
            {
                try
                {
                    _log.LogInfo($"[scheduler] due id={task.Id} → project={task.ProjectName}");
                    TaskDue?.Invoke(task);
                }
                catch (Exception ex)
                {
                    _log.LogError($"[scheduler] dispatch failed id={task.Id}: {ex.Message}");
                }
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    private AgentScheduledTask? FindUnlocked(string taskId) =>
        _tasks.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.OrdinalIgnoreCase));

    private void Load()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                _tasks = [];
                return;
            }

            var json = File.ReadAllText(_storePath);
            _tasks = JsonSerializer.Deserialize<List<AgentScheduledTask>>(json, JsonOpts) ?? [];
            _log.LogInfo($"[scheduler] loaded {_tasks.Count} task(s) from {_storePath}");
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[scheduler] load failed: {ex.Message}");
            _tasks = [];
        }
    }

    private void SaveUnlocked()
    {
        var json = JsonSerializer.Serialize(_tasks, JsonOpts);
        File.WriteAllText(_storePath, json);
    }

    private static AgentScheduledTask Clone(AgentScheduledTask t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Command = t.Command,
        ProjectName = t.ProjectName,
        CreatedBy = t.CreatedBy,
        DueAtLocal = t.DueAtLocal,
        Status = t.Status,
        CreatedAtLocal = t.CreatedAtLocal,
        FiredAtLocal = t.FiredAtLocal,
        CompletedAtLocal = t.CompletedAtLocal,
        Notes = t.Notes,
    };
}
