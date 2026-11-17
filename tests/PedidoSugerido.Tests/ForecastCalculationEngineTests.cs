using PedidoSugerido.Core.Models;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Tests;

public sealed class ForecastCalculationEngineTests
{
    [Fact]
    public void Calculate_UsesGonzalezRuizFormulaForExplicitCoverageDates()
    {
        var product = new ProductHistory(
            "P001",
            "Producto",
            "Modelo",
            709m,
            2024,
            new List<MonthlySale>
            {
                new(2024, 2, 600m),
                new(2024, 3, 600m),
                new(2024, 4, 651m),
                new(2024, 5, 370m),
                new(2024, 6, 370m),
                new(2024, 7, 370m),
                new(2024, 8, 370m),
                new(2024, 9, 374m)
            });

        var request = new PedidoSugeridoRequest(
            "input",
            "output.xlsx",
            new CoverageWindow(new DateOnly(2026, 6, 1), new DateOnly(2026, 10, 31)),
            new DateOnly(2026, 4, 15),
            new DateOnly(2026, 3, 20));

        var engine = new ForecastCalculationEngine();
        var suggestion = Assert.Single(engine.Calculate([product], request));

        Assert.Equal(1851m, suggestion.GonzalezRuiz.BridgeDemand);
        Assert.Equal(1854m, suggestion.GonzalezRuiz.CoverageDemand);
        Assert.Equal(3705m, suggestion.GonzalezRuiz.FullPeriodDemand);
        Assert.Equal(2996m, suggestion.Methods.GonzalezRuiz);
    }

    [Fact]
    public void Calculate_SupportsCoverageLongerThanOneYear()
    {
        var monthlySales = new List<MonthlySale>();
        for (var year = 2024; year <= 2025; year++)
        {
            for (var month = 0; month < 12; month++)
            {
                monthlySales.Add(new MonthlySale(year, month, 10m));
            }
        }

        var product = new ProductHistory("P002", "Producto", "Modelo", 0m, 2025, monthlySales);
        var request = new PedidoSugeridoRequest(
            "input",
            "output.xlsx",
            new CoverageWindow(new DateOnly(2026, 4, 1), new DateOnly(2027, 4, 30)),
            new DateOnly(2026, 3, 20),
            new DateOnly(2026, 3, 20));

        var engine = new ForecastCalculationEngine();
        var suggestion = Assert.Single(engine.Calculate([product], request));

        Assert.Equal(2, suggestion.GonzalezRuiz.HistoricalWindowsUsed);
        Assert.Equal(140m, suggestion.Methods.GonzalezRuiz);
        Assert.Equal(130m, suggestion.Methods.Average);
        Assert.Equal(130m, suggestion.Methods.SeasonalComparable);
    }
}
