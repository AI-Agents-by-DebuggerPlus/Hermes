using System.Data;
using System.Globalization;

namespace Hermes.Wpf.Services.Spreadsheet;

public sealed class SpreadsheetPeriodSummary
{
    public int RowCount { get; init; }

    public double HoursTotal { get; init; }

    public bool HasHoursColumn { get; init; }

    public static SpreadsheetPeriodSummary Compute(DataView view)
    {
        var hoursTotal = 0.0;
        var hasHours = view.Table?.Columns.Contains("Hours") == true;
        var rowCount = 0;

        foreach (DataRowView rowView in view)
        {
            rowCount++;
            if (!hasHours)
            {
                continue;
            }

            var value = rowView["Hours"];
            if (value is double d)
            {
                hoursTotal += d;
            }
            else if (value is float f)
            {
                hoursTotal += f;
            }
            else if (value is int i)
            {
                hoursTotal += i;
            }
            else if (value is decimal m)
            {
                hoursTotal += (double)m;
            }
            else if (value is not DBNull and not null
                     && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                hoursTotal += parsed;
            }
        }

        return new SpreadsheetPeriodSummary
        {
            RowCount = rowCount,
            HoursTotal = hoursTotal,
            HasHoursColumn = hasHours,
        };
    }
}
