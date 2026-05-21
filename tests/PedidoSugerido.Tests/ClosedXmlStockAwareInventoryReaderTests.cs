using ClosedXML.Excel;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Tests;

public sealed class ClosedXmlStockAwareInventoryReaderTests
{
    [Fact]
    public void ReadFromDirectory_ParsesNewFormat_AndImputesStockoutMonths()
    {
        var directory = CreateTempDirectory();
        var file2025 = Path.Combine(directory, "estadistico_ventas_stock_2025.xlsx");

        var sales = new decimal?[12] { 10, 12, null, null, 14, 16, 18, 20, 22, 24, 26, 28 };
        var stocks = new decimal?[12] { 100, 90, 0m, 0m, 80, 70, 60, 50, 40, 30, 20, 10 };

        CreateStockAwareWorkbook(file2025, "KUM_2025", "Total_CantVendida_2025", stockActual: 200, sales, stocks);

        var reader = new ClosedXmlStockAwareInventoryReader();
        var dataset = reader.ReadFromDirectory(directory);

        var product = Assert.Single(dataset.Products);
        Assert.Equal("P001", product.Code);
        Assert.Equal("Modelo X", product.Model);
        Assert.Equal("Producto X", product.Name);
        Assert.Equal(200m, product.CurrentStock);
        Assert.Equal(2025, product.StockYear);

        Assert.Equal(12, product.Sales.Count);

        var inStockValues = new[] { 10m, 12m, 14m, 16m, 18m, 20m, 22m, 24m, 26m, 28m };
        var expectedFallback = inStockValues.OrderBy(x => x).ToArray();
        var expectedMedian = (expectedFallback[4] + expectedFallback[5]) / 2m;

        var marchImputed = product.Sales.Single(x => x.MonthIndex == 2);
        var aprilImputed = product.Sales.Single(x => x.MonthIndex == 3);
        Assert.Equal(expectedMedian, marchImputed.Value);
        Assert.Equal(expectedMedian, aprilImputed.Value);
    }

    [Fact]
    public void ReadFromDirectory_UsesSameMonthMedian_FromOtherYears()
    {
        var directory = CreateTempDirectory();

        var sales2024 = new decimal?[12] { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var stocks2024 = new decimal?[12] { 100, 95, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45 };
        var sales2025 = new decimal?[12] { 0, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42 };
        var stocks2025 = new decimal?[12] { 0, 100, 95, 90, 85, 80, 75, 70, 65, 60, 55, 50 };

        CreateStockAwareWorkbook(
            Path.Combine(directory, "estadistico_2024.xlsx"),
            "KUM_2024", "Total_CantVendida_2024", stockActual: 0, sales2024, stocks2024);
        CreateStockAwareWorkbook(
            Path.Combine(directory, "estadistico_2025.xlsx"),
            "KUM_2025", "Total_CantVendida_2025", stockActual: 300, sales2025, stocks2025);

        var reader = new ClosedXmlStockAwareInventoryReader();
        var dataset = reader.ReadFromDirectory(directory);

        var product = Assert.Single(dataset.Products);
        Assert.Equal(300m, product.CurrentStock);
        Assert.Equal(2025, product.StockYear);

        var janStockout = product.Sales.Single(x => x.Year == 2025 && x.MonthIndex == 0);
        Assert.Equal(5m, janStockout.Value);
    }

    private static void CreateStockAwareWorkbook(
        string path,
        string sheetName,
        string totalHeader,
        decimal stockActual,
        decimal?[] monthlySales,
        decimal?[] monthlyStartStock)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName);

        ws.Cell(1, 1).Value = "Codigo";
        ws.Cell(1, 2).Value = "Modelo";
        ws.Cell(1, 3).Value = "Producto";
        ws.Cell(1, 4).Value = "StockActual";
        for (var i = 0; i < 12; i++)
        {
            ws.Cell(1, 5 + (i * 2)).Value = $"Mes{i + 1}_CantVendida";
            ws.Cell(1, 6 + (i * 2)).Value = $"Mes{i + 1}_StockInicial";
        }
        ws.Cell(1, 29).Value = totalHeader;
        ws.Cell(1, 30).Value = "Stock_Inicial_Ene";
        ws.Cell(1, 31).Value = "Stock_Inicial_Dic";

        ws.Cell(2, 1).Value = "P001";
        ws.Cell(2, 2).Value = "Modelo X";
        ws.Cell(2, 3).Value = "Producto X";
        ws.Cell(2, 4).Value = stockActual;

        for (var i = 0; i < 12; i++)
        {
            var salesCol = 5 + (i * 2);
            var stockCol = salesCol + 1;
            if (monthlySales[i].HasValue)
            {
                ws.Cell(2, salesCol).Value = monthlySales[i]!.Value;
            }
            if (monthlyStartStock[i].HasValue)
            {
                ws.Cell(2, stockCol).Value = monthlyStartStock[i]!.Value;
            }
        }

        workbook.SaveAs(path);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PedidoSugeridoStockAwareTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
