using PedidoSugerido.Core.Models;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Tests;

public sealed class StockoutImputationServiceTests
{
    [Fact]
    public void Impute_ReplacesStockoutMonths_WithSameMonthMedianFromOtherYears()
    {
        var samples = new List<MonthlyStockSample>
        {
            new(2024, 0, Sales: 10, StartStock: 50),
            new(2024, 1, Sales: 12, StartStock: 40),
            new(2025, 0, Sales: 0, StartStock: 0),
            new(2025, 1, Sales: 14, StartStock: 30),
            new(2026, 0, Sales: 20, StartStock: 60),
            new(2026, 1, Sales: 16, StartStock: 25),
        };

        var service = new StockoutImputationService();
        var result = service.Impute(samples);

        var stockoutMonth = result.Single(x => x.Year == 2025 && x.MonthIndex == 0);
        Assert.Equal(15m, stockoutMonth.Value);
    }

    [Fact]
    public void Impute_KeepsRealSales_ForInStockMonths()
    {
        var samples = new List<MonthlyStockSample>
        {
            new(2025, 0, Sales: 7, StartStock: 100),
            new(2025, 1, Sales: 9, StartStock: 50),
        };

        var service = new StockoutImputationService();
        var result = service.Impute(samples);

        Assert.Equal(7m, result.Single(x => x.MonthIndex == 0).Value);
        Assert.Equal(9m, result.Single(x => x.MonthIndex == 1).Value);
    }

    [Fact]
    public void Impute_FallsBackToGlobalMedian_WhenNoSameMonthHistory()
    {
        var samples = new List<MonthlyStockSample>
        {
            new(2025, 0, Sales: 0, StartStock: 0),
            new(2025, 1, Sales: 4, StartStock: 50),
            new(2025, 2, Sales: 8, StartStock: 50),
            new(2025, 3, Sales: 12, StartStock: 50),
        };

        var service = new StockoutImputationService();
        var result = service.Impute(samples);

        var stockout = result.Single(x => x.MonthIndex == 0);
        Assert.Equal(8m, stockout.Value);
    }

    [Fact]
    public void Impute_ReturnsZero_WhenAllMonthsAreStockout()
    {
        var samples = new List<MonthlyStockSample>
        {
            new(2025, 0, Sales: 0, StartStock: 0),
            new(2025, 1, Sales: 0, StartStock: 0),
            new(2025, 2, Sales: null, StartStock: 0),
        };

        var service = new StockoutImputationService();
        var result = service.Impute(samples);

        Assert.Equal(3, result.Count);
        Assert.All(result, x => Assert.Equal(0m, x.Value));
    }

    [Fact]
    public void Impute_KeepsPartialStockoutSales_WhenStockZeroButSalesPositive()
    {
        var samples = new List<MonthlyStockSample>
        {
            new(2024, 0, Sales: 30, StartStock: 100),
            new(2025, 0, Sales: 5, StartStock: 0),
        };

        var service = new StockoutImputationService();
        var result = service.Impute(samples);

        Assert.Equal(5m, result.Single(x => x.Year == 2025).Value);
    }

    [Fact]
    public void Impute_SkipsMonths_WithNullSalesAndUnknownStock()
    {
        var samples = new List<MonthlyStockSample>
        {
            new(2025, 0, Sales: 4, StartStock: 50),
            new(2025, 1, Sales: null, StartStock: null),
        };

        var service = new StockoutImputationService();
        var result = service.Impute(samples);

        var only = Assert.Single(result);
        Assert.Equal(0, only.MonthIndex);
    }

    [Fact]
    public void Impute_ImputesNullSales_WhenStockIsZero()
    {
        var samples = new List<MonthlyStockSample>
        {
            new(2024, 0, Sales: 6, StartStock: 30),
            new(2024, 1, Sales: 8, StartStock: 30),
            new(2025, 0, Sales: null, StartStock: 0),
            new(2025, 1, Sales: 10, StartStock: 30),
        };

        var service = new StockoutImputationService();
        var result = service.Impute(samples);

        var imputed = result.Single(x => x.Year == 2025 && x.MonthIndex == 0);
        Assert.Equal(6m, imputed.Value);
    }
}
