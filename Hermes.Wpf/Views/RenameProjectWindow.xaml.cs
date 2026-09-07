using System.Windows;
using System.Windows.Input;

namespace Hermes.Wpf.Views;

public partial class RenameProjectWindow : Window
{
    public RenameProjectWindow(string currentName)
    {
        InitializeComponent();
        CurrentNameText.Text = currentName;
        NameBox.Text = currentName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    public string NewName => (NameBox.Text ?? string.Empty).Trim();

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            MessageBox.Show(this, "Введите новое имя проекта.", "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void NameBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_OnClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
