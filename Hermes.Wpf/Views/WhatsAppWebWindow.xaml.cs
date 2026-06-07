using System.ComponentModel;
using System.Windows;

namespace Hermes.Wpf.Views;

public partial class WhatsAppWebWindow : Window
{
    private bool _forceClose;

    public WhatsAppWebWindow()
    {
        InitializeComponent();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
