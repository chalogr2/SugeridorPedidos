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
                new ForecastMethods(5m, 1m, 0m, 9m, 2m, 3m, 4m, 6m),
                new GonzalezRuizBreakdown(1m, 2m, 3m, 10m, 5m, 1),
                false,
                AbcClass.B,
                100m),
            new(
                "P002",
                "Producto 2",
                "M2",
                20m,
                new ForecastMethods(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m),
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
        Assert.Equal(11, workbook.Worksheet("Filtrado Sin Máximos").LastColumnUsed()!.ColumnNumber());
        Assert.Equal(10, workbook.Worksheet("Filtrado Sin Máx ni Claude").LastColumnUsed()!.ColumnNumber());

        var all = workbook.Worksheet("Todos los Productos");
        Assert.Equal("González Ruiz", all.Cell(1, 5).GetString());
        Assert.Equal("González Ruiz per-month", all.Cell(1, 6).GetString());
        Assert.Equal("Recomendación Claude", all.Cell(1, 12).GetString());
        Assert.Equal(5, all.Cell(2, 5).GetValue<int>());
        Assert.Equal(6, all.Cell(2, 6).GetValue<int>());
        Assert.Equal(4, all.Cell(2, 12).GetValue<int>());
    }

    [Fact]
    public void Write_RefusesToOverwriteFileThatIsNotAReport()
    {
        var outputPath = Path.Combine(CreateTempDirectory(), "ventas_2025.xlsx");
        using (var sales = new XLWorkbook())
        {
            sales.Worksheets.Add("Datos 2025").Cell(1, 1).Value = "Codigo";
            sales.SaveAs(outputPath);
        }

        var before = File.ReadAllBytes(outputPath);
        var error = Assert.Throws<InvalidOperationException>(
            () => new ClosedXmlReportWriter().Write(outputPath, [SampleSuggestion()]));

        Assert.Contains("Refusing to overwrite", error.Message);
        Assert.Equal(before, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public void Write_RefusesToOverwriteNonExcelFile()
    {
        var outputPath = Path.Combine(CreateTempDirectory(), "notas.xlsx");
        File.WriteAllText(outputPath, "not a workbook");

        Assert.Throws<InvalidOperationException>(
            () => new ClosedXmlReportWriter().Write(outputPath, [SampleSuggestion()]));
        Assert.Equal("not a workbook", File.ReadAllText(outputPath));
    }

    [Fact]
    public void Write_ReplacesPreviousReport()
    {
        var outputPath = Path.Combine(CreateTempDirectory(), "pedido.xlsx");
        var writer = new ClosedXmlReportWriter();
        writer.Write(outputPath, [SampleSuggestion("OLD")]);

        writer.Write(outputPath, [SampleSuggestion("NEW")]);

        using var workbook = new XLWorkbook(outputPath);
        Assert.Equal("NEW", workbook.Worksheet("Todos los Productos").Cell(2, 1).GetString());
    }

    private static ProductSuggestion SampleSuggestion(string code = "P001") =>
        new(
            code,
            "Producto",
            "M1",
            10m,
            new ForecastMethods(5m, 1m, 0m, 9m, 2m, 3m, 4m, 6m),
            new GonzalezRuizBreakdown(1m, 2m, 3m, 10m, 5m, 1),
            false,
            AbcClass.B,
            100m);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PedidoSugeridoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
