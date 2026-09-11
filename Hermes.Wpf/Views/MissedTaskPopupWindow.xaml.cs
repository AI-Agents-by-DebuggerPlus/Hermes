using System.Media;
using System.Windows;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Views;

public enum MissedTaskPopupOutcome
{
    Later,
    Completed,
    RunNow,
}

public partial class MissedTaskPopupWindow : Window
{
    public MissedScheduledTaskInfo TaskInfo { get; }

    public MissedTaskPopupOutcome Outcome { get; private set; } = MissedTaskPopupOutcome.Later;

    public MissedTaskPopupWindow(MissedScheduledTaskInfo task)
    {
        InitializeComponent();
        TaskInfo = task;
        Title = task.Title;
        TitleText.Text = task.Title;
        DetailText.Text = task.Detail;
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            // ignore
        }
    }

    private void RunNow_OnClick(object sender, RoutedEventArgs e)
    {
        Outcome = MissedTaskPopupOutcome.RunNow;
        DialogResult = true;
        Close();
    }

    private void Completed_OnClick(object sender, RoutedEventArgs e)
    {
        Outcome = MissedTaskPopupOutcome.Completed;
        DialogResult = true;
        Close();
    }

    private void Later_OnClick(object sender, RoutedEventArgs e)
    {
        Outcome = MissedTaskPopupOutcome.Later;
        DialogResult = false;
        Close();
    }
}
