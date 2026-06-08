using System.Data;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace Hermes.Wpf.Services.Spreadsheet;

public sealed class ExcelWorkbookDocument
{
    public required string FilePath { get; init; }

    public required IReadOnlyList<ExcelSheetTable> Sheets { get; init; }
}

public sealed class ExcelSheetTable
{
    public required string Name { get; init; }

    public required DataTable Table { get; init; }
}

public static class ExcelWorkbookReader
{
    private static readonly string[] DefaultThreeColumnNames = ["Date", "Weekday", "Hours"];

    public static ExcelWorkbookDocument Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Spreadsheet file not found.", filePath);
        }

        using var workbook = new XLWorkbook(filePath);
        var sheets = new List<ExcelSheetTable>();

        foreach (var worksheet in workbook.Worksheets)
        {
            sheets.Add(new ExcelSheetTable
            {
                Name = worksheet.Name,
                Table = ReadSheet(worksheet),
            });
        }

        return new ExcelWorkbookDocument
        {
            FilePath = filePath,
            Sheets = sheets,
        };
    }

    private static DataTable ReadSheet(IXLWorksheet worksheet)
    {
        var range = worksheet.RangeUsed();
        var table = new DataTable(worksheet.Name);

        if (range is null)
        {
            return table;
        }

        var columnCount = range.ColumnCount();
        var columnNames = BuildColumnNames(columnCount);

        foreach (var name in columnNames)
        {
            table.Columns.Add(name, typeof(object));
        }

        foreach (var row in range.RowsUsed())
        {
            var dataRow = table.NewRow();
            for (var col = 1; col <= columnCount; col++)
            {
                dataRow[col - 1] = ReadCellValue(row.Cell(col));
            }

            table.Rows.Add(dataRow);
        }

        return table;
    }

    private static string[] BuildColumnNames(int columnCount)
    {
        if (columnCount == 3)
        {
            return DefaultThreeColumnNames;
        }

        var names = new string[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            names[i] = ColumnLetter(i + 1);
        }

        return names;
    }

    private static string ColumnLetter(int index)
    {
        var dividend = index;
        var letters = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            letters = Convert.ToChar('A' + modulo) + letters;
            dividend = (dividend - modulo) / 26;
        }

        return letters;
    }

    private static object ReadCellValue(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return DBNull.Value;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return cell.GetDateTime();
        }

        if (cell.DataType == XLDataType.Number)
        {
            return cell.GetDouble();
        }

        if (cell.DataType == XLDataType.Boolean)
        {
            return cell.GetBoolean();
        }

        var text = cell.GetFormattedString(CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(text) ? DBNull.Value : text.Trim();
    }
}
