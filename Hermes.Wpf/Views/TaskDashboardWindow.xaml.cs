using System.Diagnostics;
using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class TaskDashboardWindow : Window
{
    private readonly AgentTaskSchedulerService _scheduler;
    private readonly Func<AgentScheduledTask, Task> _runNow;

    public TaskDashboardWindow(AgentTaskSchedulerService scheduler, Func<AgentScheduledTask, Task> runNow)
    {
        InitializeComponent();
        _scheduler = scheduler;
        _runNow = runNow;
        PathLabel.Text = _scheduler.StorePath;
        _scheduler.Changed += OnChanged;
        Closed += (_, _) => _scheduler.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        var all = _scheduler.GetAll();
        ScheduledList.ItemsSource = all
            .Where(t => t.Status == AgentTaskStatus.Scheduled && !t.IsOverdue)
            .Select(Wrap)
            .ToList();
        CurrentList.ItemsSource = all
            .Where(t => t.IsOverdue
                        || t.Status is AgentTaskStatus.Fired
                            or AgentTaskStatus.Completed
                            or AgentTaskStatus.Cancelled)
            .OrderByDescending(t => t.DueAtLocal)
            .Take(40)
            .Select(Wrap)
            .ToList();
    }

    private static TaskRow Wrap(AgentScheduledTask t) => new(t);

    private AgentScheduledTask? SelectedTask()
    {
        if (CurrentList.SelectedItem is TaskRow cur)
        {
            return cur.Task;
        }

        if (ScheduledList.SelectedItem is TaskRow sch)
        {
            return sch.Task;
        }

        return null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void OpenStore_Click(object sender, RoutedEventArgs e)
    {
        var dir = System.IO.Path.GetDirectoryName(_scheduler.StorePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
    }

    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        var task = SelectedTask();
        if (task is null)
        {
            MessageBox.Show(this, "Выберите задачу.", "Dashboard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _runNow(task).ConfigureAwait(true);
        Refresh();
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        var task = SelectedTask();
        if (task is null)
        {
            return;
        }

        _scheduler.TryComplete(task.Id, out _);
        Refresh();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        var task = SelectedTask();
        if (task is null)
        {
            return;
        }

        _scheduler.TryCancel(task.Id, out _);
        Refresh();
    }

    private sealed class TaskRow
    {
        public TaskRow(AgentScheduledTask task) => Task = task;

        public AgentScheduledTask Task { get; }

        public string DisplayLine =>
            $"{Task.DueAtLocal:dd.MM HH:mm} [{Task.Status}] {Task.ProjectName} — {Task.Title} ({Task.Id})";
    }
}
