using ClosedXML.Excel;
using PedidoSugerido.Core.Contracts;
using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Services;

public sealed class ClosedXmlReportWriter : IExcelReportWriter
{
    private static readonly string[] FullHeaders =
    [
        "Código",
        "Producto",
        "Modelo",
        "Saldo Actual",
        "González Ruiz",
        "Promedio",
        "Mínimos",
        "Máximos",
        "Exp Smoothing",
        "Estacional",
        "Recomendación Claude"
    ];

    private static readonly double[] FullColumnWidths = [14, 45, 14, 14, 16, 16, 14, 14, 18, 18, 20];

    public void Write(string outputPath, IReadOnlyCollection<ProductSuggestion> suggestions)
    {
        var ordered = suggestions.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();

        using var workbook = new XLWorkbook();
        WriteWorksheet(workbook, "Todos los Productos", ordered, includeMaximum: true, includeClaude: true);
        WriteWorksheet(workbook, "Filtrado (Pedido > 0)", ordered.Where(HasAnyPositiveOrder).ToList(), includeMaximum: true, includeClaude: true);
        WriteWorksheet(workbook, "Filtrado Sin Máximos", ordered.Where(HasAnyPositiveOrderWithoutMax).ToList(), includeMaximum: false, includeClaude: true);
        WriteWorksheet(workbook, "Filtrado Sin Máx ni Claude", ordered.Where(HasAnyPositiveOrderWithoutMaxOrClaude).ToList(), includeMaximum: false, includeClaude: false);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        workbook.SaveAs(outputPath);
    }

    private static void WriteWorksheet(XLWorkbook workbook, string sheetName, IReadOnlyList<ProductSuggestion> rows, bool includeMaximum, bool includeClaude)
    {
        var worksheet = workbook.Worksheets.Add(sheetName);
        var headers = BuildHeaders(includeMaximum, includeClaude);
        var widths = BuildColumnWidths(includeMaximum, includeClaude);

        for (var i = 0; i < headers.Count; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
            worksheet.Column(i + 1).Width = widths[i];
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            WriteDataRow(worksheet, rowIndex + 2, rows[rowIndex], includeMaximum, includeClaude);
        }

        var lastRow = Math.Max(2, rows.Count + 1);
        var lastColumn = headers.Count;
        var range = worksheet.Range(1, 1, lastRow, lastColumn);
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.Row(1).Style.Font.FontColor = XLColor.White;
        worksheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");

        var gonzalezColumn = 5;
        worksheet.Cell(1, gonzalezColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#548235");
        if (rows.Count > 0)
        {
            worksheet.Range(2, gonzalezColumn, rows.Count + 1, gonzalezColumn)
                .Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EFDA");
        }

        if (includeClaude)
        {
            var claudeColumn = includeMaximum ? 11 : 10;
            worksheet.Cell(1, claudeColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#2E75B6");
            if (rows.Count > 0)
            {
                worksheet.Range(2, claudeColumn, rows.Count + 1, claudeColumn)
                    .Style.Fill.BackgroundColor = XLColor.FromHtml("#D6E4F0");
            }
        }

        worksheet.SheetView.FreezeRows(1);
        range.SetAutoFilter();
    }

    private static List<string> BuildHeaders(bool includeMaximum, bool includeClaude)
    {
        var headers = new List<string>
        {
            "Código",
            "Producto",
            "Modelo",
            "Saldo Actual",
            "González Ruiz",
            "Promedio",
            "Mínimos"
        };

        if (includeMaximum)
        {
            headers.Add("Máximos");
        }

        headers.Add("Exp Smoothing");
        headers.Add("Estacional");

        if (includeClaude)
        {
            headers.Add("Recomendación Claude");
        }

        return headers;
    }

    private static List<double> BuildColumnWidths(bool includeMaximum, bool includeClaude)
    {
        var indexes = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
        if (includeMaximum)
        {
            indexes.Add(7);
        }

        indexes.Add(8);
        indexes.Add(9);

        if (includeClaude)
        {
            indexes.Add(10);
        }

        return indexes.Select(i => FullColumnWidths[i]).ToList();
    }

    private static void WriteDataRow(IXLWorksheet worksheet, int rowNumber, ProductSuggestion row, bool includeMaximum, bool includeClaude)
    {
        var column = 1;
        worksheet.Cell(rowNumber, column++).Value = row.Code;
        worksheet.Cell(rowNumber, column++).Value = row.Name;
        worksheet.Cell(rowNumber, column++).Value = row.Model;
        worksheet.Cell(rowNumber, column++).Value = row.CurrentStock;
        worksheet.Cell(rowNumber, column++).Value = row.Methods.GonzalezRuiz;
        worksheet.Cell(rowNumber, column++).Value = row.Methods.Average;
        worksheet.Cell(rowNumber, column++).Value = row.Methods.Minimum;
        if (includeMaximum)
        {
            worksheet.Cell(rowNumber, column++).Value = row.Methods.Maximum;
        }
        worksheet.Cell(rowNumber, column++).Value = row.Methods.ExponentialSmoothing;
        worksheet.Cell(rowNumber, column++).Value = row.Methods.SeasonalComparable;
        if (includeClaude)
        {
            worksheet.Cell(rowNumber, column).Value = row.Methods.WeightedRecommendation;
        }
    }

    private static bool HasAnyPositiveOrder(ProductSuggestion item) =>
        item.Methods.GonzalezRuiz > 0 ||
        item.Methods.Average > 0 ||
        item.Methods.Minimum > 0 ||
        item.Methods.Maximum > 0 ||
        item.Methods.ExponentialSmoothing > 0 ||
        item.Methods.SeasonalComparable > 0 ||
        item.Methods.WeightedRecommendation > 0;

    private static bool HasAnyPositiveOrderWithoutMax(ProductSuggestion item) =>
        item.Methods.GonzalezRuiz > 0 ||
        item.Methods.Average > 0 ||
        item.Methods.Minimum > 0 ||
        item.Methods.ExponentialSmoothing > 0 ||
        item.Methods.SeasonalComparable > 0 ||
        item.Methods.WeightedRecommendation > 0;

    private static bool HasAnyPositiveOrderWithoutMaxOrClaude(ProductSuggestion item) =>
        item.Methods.GonzalezRuiz > 0 ||
        item.Methods.Average > 0 ||
        item.Methods.Minimum > 0 ||
        item.Methods.ExponentialSmoothing > 0 ||
        item.Methods.SeasonalComparable > 0;
}
