using ClosedXML.Excel;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Tests;

public sealed class ClosedXmlInventoryReaderTests
{
    [Fact]
    public void ReadFromDirectory_ConsolidatesFilesAndUsesLatestStock()
    {
        var directory = CreateTempDirectory();
        var file2024 = Path.Combine(directory, "ventas_2024.xlsx");
        var file2025 = Path.Combine(directory, "ventas_2025.xlsx");

        CreateWorkbook(
            file2024,
            "Datos 2024",
            "TotalAnual2024",
            stock: 10,
            monthlyValues: ["no data", 5, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        CreateWorkbook(
            file2025,
            "Datos 2025",
            "TotalAnual2025",
            stock: 99,
            monthlyValues: Enumerable.Repeat<object>(1m, 12).ToArray(),
            includeControlAndExcludedSheets: true);

        var reader = new ClosedXmlInventoryReader();
        var result = reader.ReadFromDirectory(directory);

        var product = Assert.Single(result.Products);
        Assert.Equal(2, result.SourceFiles.Count);
        Assert.Equal("P001", product.Code);
        Assert.Equal(99, product.CurrentStock);
        Assert.Equal(2025, product.StockYear);
        Assert.Contains(product.Sales, x => x.Year == 2024 && x.MonthIndex == 1 && x.Value == 5);
        Assert.DoesNotContain(product.Sales, x => x.Year == 2024 && x.MonthIndex == 0);
    }

    private static void CreateWorkbook(
        string path,
        string worksheetName,
        string annualHeader,
        decimal stock,
        object[] monthlyValues,
        bool includeControlAndExcludedSheets = false)
    {
        using var workbook = new XLWorkbook();
        if (includeControlAndExcludedSheets)
        {
            workbook.Worksheets.Add("ExcluTmp");
        }

        var ws = workbook.Worksheets.Add(worksheetName);
        WriteHeaders(ws, annualHeader);
        ws.Cell(2, 1).Value = "P001";
        ws.Cell(2, 2).Value = "Producto";
        ws.Cell(2, 3).Value = "Modelo";
        ws.Cell(2, 4).Value = stock;

        for (var i = 0; i < 12; i++)
        {
            if (monthlyValues[i] is string textValue)
            {
                ws.Cell(2, 5 + i).Value = textValue;
            }
            else if (monthlyValues[i] is decimal decimalValue)
            {
                ws.Cell(2, 5 + i).Value = decimalValue;
            }
            else if (monthlyValues[i] is int intValue)
            {
                ws.Cell(2, 5 + i).Value = intValue;
            }
        }

        workbook.SaveAs(path);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PedidoSugeridoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteHeaders(IXLWorksheet ws, string annualHeader)
    {
        ws.Cell(1, 1).Value = "Codigo";
        ws.Cell(1, 2).Value = "Nombre";
        ws.Cell(1, 3).Value = "Modelo";
        ws.Cell(1, 4).Value = "Saldo";

        for (var month = 0; month < 12; month++)
        {
            ws.Cell(1, 5 + month).Value = $"Mes{month + 1}";
        }

        ws.Cell(1, 17).Value = annualHeader;
    }
}
