using System.Windows;

namespace Hermes.Wpf.Views;

public partial class EditRetryMessageWindow : Window
{
    public EditRetryMessageWindow(string currentText)
    {
        InitializeComponent();
        BodyBox.Text = currentText ?? string.Empty;
        Loaded += (_, _) =>
        {
            BodyBox.CaretIndex = BodyBox.Text.Length;
            BodyBox.Focus();
        };
    }

    public string EditedText => (BodyBox.Text ?? string.Empty).Trim();

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EditedText))
        {
            MessageBox.Show(
                this,
                "Сообщение не может быть пустым.",
                "Edit & retry",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
