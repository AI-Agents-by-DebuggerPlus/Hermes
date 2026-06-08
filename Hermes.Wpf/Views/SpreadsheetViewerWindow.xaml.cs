using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Hermes.Wpf.Services;
using Hermes.Wpf.Services.Spreadsheet;
using Microsoft.Win32;

namespace Hermes.Wpf.Views;

public partial class SpreadsheetViewerWindow : Window
{
    private const string DefaultWorkbookPath =
        @"G:\My Drive\My Projects\Hermes\Working_days_2026_FullCalendar_edited_v2.xlsx";

    private ExcelWorkbookDocument? _document;
    private DataTable? _activeTable;

    public SpreadsheetViewerWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DwmDarkTitleBar.Apply(this);

        if (File.Exists(DefaultWorkbookPath))
        {
            TryLoadWorkbook(DefaultWorkbookPath);
        }
    }

    private void OpenFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            Title = "Открыть таблицу",
        };

        if (!string.IsNullOrWhiteSpace(_document?.FilePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_document.FilePath);
            dialog.FileName = Path.GetFileName(_document.FilePath);
        }
        else if (File.Exists(DefaultWorkbookPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(DefaultWorkbookPath);
            dialog.FileName = Path.GetFileName(DefaultWorkbookPath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        TryLoadWorkbook(dialog.FileName);
    }

    private void TryLoadWorkbook(string path)
    {
        try
        {
            _document = ExcelWorkbookReader.Load(path);
            FilePathText.Text = path;
            SheetComboBox.ItemsSource = _document.Sheets.Select(s => s.Name).ToList();
            if (_document.Sheets.Count > 0)
            {
                SheetComboBox.SelectedIndex = 0;
            }

            Title = $"Просмотр таблицы — {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Не удалось открыть файл",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SheetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_document is null || SheetComboBox.SelectedItem is not string sheetName)
        {
            return;
        }

        var sheet = _document.Sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            return;
        }

        _activeTable = sheet.Table;
        ClearFilterControls();
        BindGrid(_activeTable.DefaultView);
    }

    private void ApplyFilterButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeTable is null)
        {
            return;
        }

        var view = _activeTable.DefaultView;
        if (!TryBuildDateFilter(out var filter))
        {
            view.RowFilter = string.Empty;
        }
        else
        {
            view.RowFilter = filter;
        }

        UpdateSummary(view);
    }

    private void ClearFilterButton_OnClick(object sender, RoutedEventArgs e)
    {
        ClearFilterControls();
        if (_activeTable is null)
        {
            return;
        }

        var view = _activeTable.DefaultView;
        view.RowFilter = string.Empty;
        UpdateSummary(view);
    }

    private void ClearFilterControls()
    {
        FilterFromPicker.SelectedDate = null;
        FilterToPicker.SelectedDate = null;
    }

    private bool TryBuildDateFilter(out string filter)
    {
        filter = string.Empty;
        if (_activeTable is null || !_activeTable.Columns.Contains("Date"))
        {
            return false;
        }

        var from = FilterFromPicker.SelectedDate;
        var to = FilterToPicker.SelectedDate;
        if (from is null && to is null)
        {
            return false;
        }

        var parts = new List<string>();
        if (from is not null)
        {
            parts.Add($"[Date] >= #{from.Value:MM/dd/yyyy}#");
        }

        if (to is not null)
        {
            parts.Add($"[Date] <= #{to.Value:MM/dd/yyyy}#");
        }

        filter = string.Join(" AND ", parts);
        return true;
    }

    private void BindGrid(DataView view)
    {
        SheetGrid.Columns.Clear();
        if (view.Table is null)
        {
            SheetGrid.ItemsSource = null;
            SummaryText.Text = string.Empty;
            return;
        }

        foreach (DataColumn column in view.Table.Columns)
        {
            var binding = new Binding(column.ColumnName)
            {
                TargetNullValue = string.Empty,
            };

            if (column.DataType == typeof(DateTime))
            {
                binding.StringFormat = "yyyy-MM-dd";
            }
            else if (column.DataType == typeof(double) || column.DataType == typeof(float) || column.DataType == typeof(decimal))
            {
                binding.StringFormat = "0.##";
            }

            SheetGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.ColumnName,
                Binding = binding,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        }

        SheetGrid.ItemsSource = view;
        UpdateSummary(view);
    }

    private void UpdateSummary(DataView view)
    {
        var summary = SpreadsheetPeriodSummary.Compute(view);
        if (!summary.HasHoursColumn)
        {
            SummaryText.Text = $"Строк: {summary.RowCount}";
            return;
        }

        SummaryText.Text =
            $"Строк: {summary.RowCount} · Часы: {summary.HoursTotal.ToString("0.##", CultureInfo.CurrentCulture)}";
    }
}
