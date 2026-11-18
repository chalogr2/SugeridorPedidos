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

    [Theory]
    [InlineData("SaldoActual")]
    [InlineData("Saldo")]
    [InlineData("Saldo Actual")]
    [InlineData("SALDOACTUAL")]
    [InlineData("SaldoActual ")]
    [InlineData("Sáldo actual")]
    [InlineData("Stock")]
    [InlineData("Stock Actual")]
    [InlineData("Existencia")]
    [InlineData("Existencias")]
    [InlineData("Inventario")]
    [InlineData("INVENTARIO ACTUAL")]
    public void ReadFromDirectory_AcceptsStockHeaderVariants_WithoutShiftingMonths(string stockHeader)
    {
        var directory = CreateTempDirectory();
        CreateWorkbook(
            Path.Combine(directory, "ventas_2025.xlsx"),
            "Datos 2025",
            "TotalAnual2025",
            stock: 99,
            monthlyValues: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
            stockHeader: stockHeader);

        var product = Assert.Single(new ClosedXmlInventoryReader().ReadFromDirectory(directory).Products);

        Assert.Equal(99, product.CurrentStock);
        Assert.Equal(12, product.Sales.Count);
        Assert.Contains(product.Sales, x => x.MonthIndex == 0 && x.Value == 1);
        Assert.Contains(product.Sales, x => x.MonthIndex == 11 && x.Value == 12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Enero")]
    [InlineData("Cantidad")]
    public void ReadFromDirectory_RejectsUnrecognizedStockHeader(string stockHeader)
    {
        var directory = CreateTempDirectory();
        CreateWorkbook(
            Path.Combine(directory, "ventas_2025.xlsx"),
            "Datos 2025",
            "TotalAnual2025",
            stock: 99,
            monthlyValues: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
            stockHeader: stockHeader);

        var error = Assert.Throws<InvalidOperationException>(
            () => new ClosedXmlInventoryReader().ReadFromDirectory(directory));
        Assert.Contains("Column 4", error.Message);
        Assert.Contains("ventas_2025.xlsx", error.Message);
    }

    [Fact]
    public void ReadFromDirectory_RejectsTwoFilesForTheSameYear()
    {
        var directory = CreateTempDirectory();
        var months = new object[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        CreateWorkbook(Path.Combine(directory, "ventas_2025.xlsx"), "Datos 2025", "TotalAnual2025", 5, months);
        CreateWorkbook(Path.Combine(directory, "ventas_2025_corregido.xlsx"), "Datos 2025", "TotalAnual2025", 5, months);

        var error = Assert.Throws<InvalidOperationException>(
            () => new ClosedXmlInventoryReader().ReadFromDirectory(directory));

        Assert.Contains("'P001'", error.Message);
        Assert.Contains("2025", error.Message);
        Assert.Contains("ventas_2025.xlsx", error.Message);
        Assert.Contains("ventas_2025_corregido.xlsx", error.Message);
    }

    [Fact]
    public void ReadFromDirectory_RejectsDuplicateRowInOneSheet()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "ventas_2025.xlsx");
        CreateWorkbook(path, "Datos 2025", "TotalAnual2025", 5, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
        using (var workbook = new XLWorkbook(path))
        {
            var ws = workbook.Worksheet("Datos 2025");
            ws.Row(2).CopyTo(ws.Row(3));
            ws.Cell(3, 1).Value = "p001";
            workbook.Save();
        }

        var error = Assert.Throws<InvalidOperationException>(
            () => new ClosedXmlInventoryReader().ReadFromDirectory(directory));

        Assert.Contains("row 2", error.Message);
        Assert.Contains("row 3", error.Message);
    }

    [Fact]
    public void ReadFromDirectory_ReadsNumbersStoredAsText()
    {
        var directory = CreateTempDirectory();
        CreateWorkbook(
            Path.Combine(directory, "ventas_2025.xlsx"),
            "Datos 2025",
            "TotalAnual2025",
            stock: 0,
            monthlyValues: ["12,5", "1,234", "1.234,7", "7", 0, 0, 0, 0, 0, 0, 0, 0]);

        var product = Assert.Single(new ClosedXmlInventoryReader().ReadFromDirectory(directory).Products);

        Assert.Contains(product.Sales, x => x.MonthIndex == 0 && x.Value == 12);
        Assert.Contains(product.Sales, x => x.MonthIndex == 1 && x.Value == 1234);
        Assert.Contains(product.Sales, x => x.MonthIndex == 2 && x.Value == 1234);
        Assert.Contains(product.Sales, x => x.MonthIndex == 3 && x.Value == 7);
    }

    private static void CreateWorkbook(
        string path,
        string worksheetName,
        string annualHeader,
        decimal stock,
        object[] monthlyValues,
        bool includeControlAndExcludedSheets = false,
        string stockHeader = "Saldo")
    {
        using var workbook = new XLWorkbook();
        if (includeControlAndExcludedSheets)
        {
            workbook.Worksheets.Add("ExcluTmp");
        }

        var ws = workbook.Worksheets.Add(worksheetName);
        WriteHeaders(ws, annualHeader, stockHeader);
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

    private static void WriteHeaders(IXLWorksheet ws, string annualHeader, string stockHeader)
    {
        ws.Cell(1, 1).Value = "Codigo";
        ws.Cell(1, 2).Value = "Nombre";
        ws.Cell(1, 3).Value = "Modelo";
        ws.Cell(1, 4).Value = stockHeader;

        for (var month = 0; month < 12; month++)
        {
            ws.Cell(1, 5 + month).Value = $"Mes{month + 1}";
        }

        ws.Cell(1, 17).Value = annualHeader;
    }
}
