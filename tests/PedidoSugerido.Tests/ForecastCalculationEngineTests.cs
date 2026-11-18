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

    // Today Nov 2026, coverage Jan-Dec 2027: each year needs Nov, Dec (bridge) and Jan-Dec.
    private static readonly PedidoSugeridoRequest YearEndRequest = new(
        "input",
        "output.xlsx",
        new CoverageWindow(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)),
        new DateOnly(2026, 11, 17),
        new DateOnly(2026, 11, 17));

    [Fact]
    public void Calculate_GonzalezRuizIgnoresPartialYears_WhenCompleteYearsExist()
    {
        var sales = MonthlyTens(2024, 0, 11).Concat(MonthlyTens(2025, 0, 9)).ToList();
        var product = new ProductHistory("P003", "Producto", "Modelo", 0m, 2025, sales);

        var suggestion = Assert.Single(new ForecastCalculationEngine().Calculate([product], YearEndRequest));

        // 2024 only: 2 bridge months + 12 coverage months at 10 each. Averaging in 2025's
        // missing Nov-Dec as zeros would have given (140 + 100) / 2 = 120.
        Assert.Equal(1, suggestion.GonzalezRuiz.HistoricalWindowsUsed);
        Assert.False(suggestion.GonzalezRuiz.UsesIncompleteYears);
        Assert.Equal(140m, suggestion.Methods.GonzalezRuiz);
    }

    [Fact]
    public void Calculate_GonzalezRuizFallsBackToPartialYears_ForNewProducts()
    {
        var product = new ProductHistory("P004", "Producto", "Modelo", 0m, 2025, MonthlyTens(2025, 0, 9).ToList());

        var suggestion = Assert.Single(new ForecastCalculationEngine().Calculate([product], YearEndRequest));

        Assert.Equal(1, suggestion.GonzalezRuiz.HistoricalWindowsUsed);
        Assert.True(suggestion.GonzalezRuiz.UsesIncompleteYears);
        Assert.Equal(100m, suggestion.Methods.GonzalezRuiz);
    }

    // Today Nov 2026, coverage Jan-Sep 2027: period = Nov, Dec (bridge) + Jan-Sep = 11 months.
    private static readonly PedidoSugeridoRequest JanToSepRequest = new(
        "input",
        "output.xlsx",
        new CoverageWindow(new DateOnly(2027, 1, 1), new DateOnly(2027, 9, 30)),
        new DateOnly(2026, 12, 31),
        new DateOnly(2026, 11, 17));

    [Fact]
    public void Calculate_GonzalezRuizPerMonth_UsesRealMonthsOfThePartialYear()
    {
        // 2024 and 2025 sell 10 a month; 2026 (Jan-Oct only) sells 20 a month.
        var sales = MonthlyTens(2024, 0, 11)
            .Concat(MonthlyTens(2025, 0, 11))
            .Concat(Enumerable.Range(0, 10).Select(month => new MonthlySale(2026, month, 20m)))
            .ToList();
        var product = new ProductHistory("P005", "Producto", "Modelo", 0m, 2026, sales);

        var suggestion = Assert.Single(new ForecastCalculationEngine().Calculate([product], JanToSepRequest));

        // Classic: 2026 lacks Nov-Dec so only 2024 and 2025 count: 11 months x 10 = 110.
        Assert.Equal(110m, suggestion.Methods.GonzalezRuiz);
        // Per-month: Nov and Dec average 2024-2025 (10 each); Jan-Sep average all three years
        // ((10 + 10 + 20) / 3 each): 20 + 9 x 13.33 = 140.
        Assert.Equal(140m, suggestion.Methods.GonzalezRuizPerMonth);
    }

    [Fact]
    public void Calculate_GonzalezRuizPerMonth_MatchesClassic_WhenAllYearsAreComplete()
    {
        var sales = MonthlyTens(2024, 0, 11).Concat(MonthlyTens(2025, 0, 11)).ToList();
        var product = new ProductHistory("P006", "Producto", "Modelo", 25m, 2025, sales);

        var suggestion = Assert.Single(new ForecastCalculationEngine().Calculate([product], JanToSepRequest));

        Assert.Equal(85m, suggestion.Methods.GonzalezRuiz);
        Assert.Equal(suggestion.Methods.GonzalezRuiz, suggestion.Methods.GonzalezRuizPerMonth);
    }

    [Fact]
    public void Calculate_GonzalezRuizPerMonth_FillsMonthsWithNoDataFromOverallAverage_AndSubtractsStockOnce()
    {
        // Only Jan-Jun 2025 at 6 each: Nov, Dec, Jul, Aug, Sep have no data in any year.
        var sales = Enumerable.Range(0, 6).Select(month => new MonthlySale(2025, month, 6m)).ToList();
        var product = new ProductHistory("P007", "Producto", "Modelo", 10m, 2025, sales);

        var suggestion = Assert.Single(new ForecastCalculationEngine().Calculate([product], JanToSepRequest));

        // 11 months x 6 = 66, minus stock 10.
        Assert.Equal(56m, suggestion.Methods.GonzalezRuizPerMonth);
    }

    private static IEnumerable<MonthlySale> MonthlyTens(int year, int firstMonth, int lastMonth) =>
        Enumerable.Range(firstMonth, lastMonth - firstMonth + 1).Select(month => new MonthlySale(year, month, 10m));
}
