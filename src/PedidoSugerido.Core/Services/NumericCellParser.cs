using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace PedidoSugerido.Core.Services;

// Real numeric cells are used as-is. Numbers stored as text are only accepted in unambiguous
// shapes; culture-based parsing is avoided because the invariant culture reads "12,5" as 125.
public static class NumericCellParser
{
    private static readonly Regex PlainNumber = new(@"^[+-]?\d+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex CommaThousands = new(@"^[+-]?\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex DotThousandsCommaDecimal = new(@"^[+-]?\d{1,3}(\.\d{3})+(,\d+)?$", RegexOptions.Compiled);
    private static readonly Regex CommaDecimal = new(@"^[+-]?\d+,\d+$", RegexOptions.Compiled);

    // Returns null for empty and "no data" cells; values are truncated to whole units.
    public static decimal? Parse(IXLCell cell)
    {
        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("no data", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number)
        {
            return decimal.Truncate(cell.GetValue<decimal>());
        }

        if (TryParseText(text, out var value))
        {
            return decimal.Truncate(value);
        }

        throw new InvalidOperationException(
            $"Cell '{cell.Address}' in worksheet '{cell.Worksheet.Name}' contains a non-numeric value: '{text}'.");
    }

    // "1234", "12.5" | "1,234", "1,234.5" (comma thousands) | "1.234", "1.234,5" with thousands
    // groups only when a decimal comma follows or there are several groups | "12,5" (decimal comma).
    // "1,234" is read as 1234 and "1.234" as 1.234: with a single group the plain reading wins.
    public static bool TryParseText(string text, out decimal value)
    {
        text = text.Trim();
        string normalized;

        if (PlainNumber.IsMatch(text))
        {
            normalized = text;
        }
        else if (CommaThousands.IsMatch(text))
        {
            normalized = text.Replace(",", string.Empty);
        }
        else if (DotThousandsCommaDecimal.IsMatch(text))
        {
            normalized = text.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (CommaDecimal.IsMatch(text))
        {
            normalized = text.Replace(',', '.');
        }
        else
        {
            value = 0m;
            return false;
        }

        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
