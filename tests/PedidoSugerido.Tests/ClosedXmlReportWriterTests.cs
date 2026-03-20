using ClosedXML.Excel;
using PedidoSugerido.Core.Models;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Tests;

public sealed class ClosedXmlReportWriterTests
{
    [Fact]
    public void Write_CreatesExpectedSheets()
    {
        var outputPath = Path.Combine(CreateTempDirectory(), "salida.xlsx");
        var suggestions = new List<ProductSuggestion>
        {
            new(
                "P001",
                "Producto 1",
                "M1",
                10m,
                new ForecastMethods(5m, 1m, 0m, 9m, 2m, 3m, 4m),
                new GonzalezRuizBreakdown(1m, 2m, 3m, 10m, 5m, 1),
                false,
                AbcClass.B,
                100m),
            new(
                "P002",
                "Producto 2",
                "M2",
                20m,
                new ForecastMethods(0m, 0m, 0m, 0m, 0m, 0m, 0m),
                new GonzalezRuizBreakdown(0m, 0m, 0m, 20m, 0m, 0),
                false,
                AbcClass.C,
                10m)
        };

        var writer = new ClosedXmlReportWriter();
        writer.Write(outputPath, suggestions);

        using var workbook = new XLWorkbook(outputPath);
        Assert.Equal(
            ["Todos los Productos", "Filtrado (Pedido > 0)", "Filtrado Sin Máximos", "Filtrado Sin Máx ni Claude"],
            workbook.Worksheets.Select(x => x.Name).ToArray());

        Assert.Equal(3, workbook.Worksheet("Todos los Productos").LastRowUsed()!.RowNumber());
        Assert.Equal(2, workbook.Worksheet("Filtrado (Pedido > 0)").LastRowUsed()!.RowNumber());
        Assert.Equal(10, workbook.Worksheet("Filtrado Sin Máximos").LastColumnUsed()!.ColumnNumber());
        Assert.Equal(9, workbook.Worksheet("Filtrado Sin Máx ni Claude").LastColumnUsed()!.ColumnNumber());
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PedidoSugeridoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
