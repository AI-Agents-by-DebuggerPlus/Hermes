using System.Media;
using System.Windows;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Views;

public partial class MissedTaskPopupWindow : Window
{
    public MissedScheduledTaskInfo TaskInfo { get; }

    public bool RunNowRequested { get; private set; }

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
        RunNowRequested = true;
        DialogResult = true;
        Close();
    }

    private void Later_OnClick(object sender, RoutedEventArgs e)
    {
        RunNowRequested = false;
        DialogResult = false;
        Close();
    }
}
