using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Services;

public sealed class ForecastCalculationEngine
{
    private const decimal LeadTimeDaysPerMonth = 30.44m;
    private const decimal ExponentialSmoothingAlpha = 0.3m;

    public IReadOnlyList<ProductSuggestion> Calculate(IReadOnlyList<ProductHistory> products, PedidoSugeridoRequest request)
    {
        var contexts = products
            .Select(product => BuildContext(product, request))
            .ToList();

        var totals = contexts
            .Select(x => x.TotalHistoricalDemand)
            .OrderBy(x => x)
            .ToList();

        var p50 = Statistics.Percentile(totals, 0.50m);
        var p80 = Statistics.Percentile(totals, 0.80m);

        return contexts
            .Select(context => CreateSuggestion(context, p50, p80))
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ProductComputationContext BuildContext(ProductHistory product, PedidoSugeridoRequest request)
    {
        var orderedSales = product.Sales
            .OrderBy(x => x.Year)
            .ThenBy(x => x.MonthIndex)
            .ToList();

        var allValues = orderedSales.Select(x => x.Value).ToList();
        var leadTimeMonths = request.DeliveryDate.DayNumber >= request.Today.DayNumber
            ? (request.DeliveryDate.DayNumber - request.Today.DayNumber) / LeadTimeDaysPerMonth
            : 0m;

        var coverageMonths = MonthHelper.GetInclusiveMonthSequence(
            request.Coverage.StartMonthIndex,
            request.Coverage.EndMonthIndex);

        var bridgeMonths = MonthHelper.GetBridgeMonths(
            request.Today.Month - 1,
            request.Coverage.StartMonthIndex);

        var average = allValues.Count == 0 ? 0m : allValues.Average();
        var minimum = allValues.Count == 0 ? 0m : allValues.Min();
        var maximum = allValues.Count == 0 ? 0m : allValues.Max();
        var exponential = CalculateExponentialSmoothing(allValues);
        var seasonalCoverageDemand = CalculateSeasonalCoverageDemand(orderedSales, coverageMonths);
        var gonzalez = CalculateGonzalezRuiz(product, bridgeMonths, coverageMonths, request.Today.Month - 1);
        var totalDemand = allValues.Sum();
        var isSeasonal = Statistics.CoefficientOfVariation(allValues) > 0.8m;

        return new ProductComputationContext(
            product,
            leadTimeMonths,
            coverageMonths,
            average,
            minimum,
            maximum,
            exponential,
            seasonalCoverageDemand,
            gonzalez,
            totalDemand,
            isSeasonal);
    }

    private static ProductSuggestion CreateSuggestion(ProductComputationContext context, decimal p50, decimal p80)
    {
        var abcClass = context.TotalHistoricalDemand > p80
            ? AbcClass.A
            : context.TotalHistoricalDemand > p50
                ? AbcClass.B
                : AbcClass.C;

        var coverageCount = context.CoverageMonths.Count;
        var averageOrder = CalculateReplenishmentOrder(context.AverageDemand, coverageCount, context.LeadTimeMonths, context.Product.CurrentStock);
        var minimumOrder = CalculateReplenishmentOrder(context.MinimumDemand, coverageCount, context.LeadTimeMonths, context.Product.CurrentStock);
        var maximumOrder = CalculateReplenishmentOrder(context.MaximumDemand, coverageCount, context.LeadTimeMonths, context.Product.CurrentStock);
        var exponentialOrder = CalculateReplenishmentOrder(context.ExponentialSmoothingDemand, coverageCount, context.LeadTimeMonths, context.Product.CurrentStock);

        var seasonalOrder = ClampToOrder(
            context.SeasonalCoverageDemand + (context.AverageDemand * context.LeadTimeMonths) - context.Product.CurrentStock);

        var weightedOrder = CalculateWeightedRecommendation(
            context.IsSeasonal,
            abcClass,
            context.GonzalezRuiz.FinalOrder,
            seasonalOrder,
            exponentialOrder,
            averageOrder,
            minimumOrder,
            maximumOrder);

        return new ProductSuggestion(
            context.Product.Code,
            context.Product.Name,
            context.Product.Model,
            context.Product.CurrentStock,
            new ForecastMethods(
                context.GonzalezRuiz.FinalOrder,
                averageOrder,
                minimumOrder,
                maximumOrder,
                exponentialOrder,
                seasonalOrder,
                weightedOrder),
            context.GonzalezRuiz,
            context.IsSeasonal,
            abcClass,
            context.TotalHistoricalDemand);
    }

    private static GonzalezRuizBreakdown CalculateGonzalezRuiz(
        ProductHistory product,
        IReadOnlyList<int> bridgeMonths,
        IReadOnlyList<int> coverageMonths,
        int currentMonthIndex)
    {
        var sequence = MonthHelper.GetPeriodSequence(currentMonthIndex, coverageMonths[^1]);
        var salesLookup = product.Sales.ToDictionary(x => (x.Year, x.MonthIndex), x => x.Value);
        var years = product.Sales.Select(x => x.Year).Distinct().OrderBy(x => x).ToList();
        var bridgeMonthSet = bridgeMonths.ToHashSet();
        var coverageMonthSet = coverageMonths.ToHashSet();
        var windowResults = new List<(decimal Bridge, decimal Coverage, decimal Full, decimal Order)>();

        foreach (var anchorYear in years)
        {
            decimal bridgeDemand = 0m;
            decimal coverageDemand = 0m;
            var coveragePoints = 0;

            foreach (var periodMonth in sequence)
            {
                if (!salesLookup.TryGetValue((anchorYear + periodMonth.YearOffset, periodMonth.MonthIndex), out var value))
                {
                    continue;
                }

                if (bridgeMonthSet.Contains(periodMonth.MonthIndex))
                {
                    bridgeDemand += value;
                }

                if (coverageMonthSet.Contains(periodMonth.MonthIndex))
                {
                    coverageDemand += value;
                    coveragePoints++;
                }
            }

            if (coveragePoints == 0)
            {
                continue;
            }

            var full = bridgeDemand + coverageDemand;
            windowResults.Add((bridgeDemand, coverageDemand, full, Math.Max(0m, full - product.CurrentStock)));
        }

        if (windowResults.Count == 0)
        {
            return new GonzalezRuizBreakdown(0m, 0m, 0m, product.CurrentStock, 0m, 0);
        }

        return new GonzalezRuizBreakdown(
            windowResults.Average(x => x.Bridge),
            windowResults.Average(x => x.Coverage),
            windowResults.Average(x => x.Full),
            product.CurrentStock,
            Math.Ceiling(windowResults.Average(x => x.Order)),
            windowResults.Count);
    }

    private static decimal CalculateExponentialSmoothing(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var smoothed = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            smoothed = (ExponentialSmoothingAlpha * values[i]) + ((1m - ExponentialSmoothingAlpha) * smoothed);
        }

        return smoothed;
    }

    private static decimal CalculateSeasonalCoverageDemand(IReadOnlyList<MonthlySale> sales, IReadOnlyList<int> coverageMonths)
    {
        decimal total = 0m;
        foreach (var month in coverageMonths)
        {
            var monthValues = sales.Where(x => x.MonthIndex == month).Select(x => x.Value).ToList();
            total += monthValues.Count == 0 ? 0m : monthValues.Average();
        }

        return total;
    }

    private static decimal CalculateReplenishmentOrder(decimal monthlyDemand, int coverageMonthCount, decimal leadTimeMonths, decimal stock)
    {
        var coverageDemand = monthlyDemand * coverageMonthCount;
        var leadTimeDemand = monthlyDemand * leadTimeMonths;
        return ClampToOrder(coverageDemand + leadTimeDemand - stock);
    }

    private static decimal CalculateWeightedRecommendation(
        bool isSeasonal,
        AbcClass abcClass,
        decimal gonzalez,
        decimal seasonal,
        decimal exponential,
        decimal average,
        decimal minimum,
        decimal maximum)
    {
        var weights = isSeasonal
            ? new WeightProfile(0.30m, 0.30m, 0.15m, 0.15m, 0.05m, 0.05m)
            : abcClass switch
            {
                AbcClass.A => new WeightProfile(0.30m, 0.15m, 0.25m, 0.20m, 0.05m, 0.05m),
                AbcClass.B => new WeightProfile(0.25m, 0.15m, 0.20m, 0.25m, 0.10m, 0.05m),
                _ => new WeightProfile(0.25m, 0.15m, 0.15m, 0.20m, 0.20m, 0.05m)
            };

        var weighted =
            (gonzalez * weights.GonzalezRuiz) +
            (seasonal * weights.SeasonalComparable) +
            (exponential * weights.ExponentialSmoothing) +
            (average * weights.Average) +
            (minimum * weights.Minimum) +
            (maximum * weights.Maximum);

        return ClampToOrder(weighted);
    }

    private static decimal ClampToOrder(decimal raw) => raw <= 0m ? 0m : Math.Ceiling(raw);

    private sealed record ProductComputationContext(
        ProductHistory Product,
        decimal LeadTimeMonths,
        IReadOnlyList<int> CoverageMonths,
        decimal AverageDemand,
        decimal MinimumDemand,
        decimal MaximumDemand,
        decimal ExponentialSmoothingDemand,
        decimal SeasonalCoverageDemand,
        GonzalezRuizBreakdown GonzalezRuiz,
        decimal TotalHistoricalDemand,
        bool IsSeasonal);

    private sealed record WeightProfile(
        decimal GonzalezRuiz,
        decimal SeasonalComparable,
        decimal ExponentialSmoothing,
        decimal Average,
        decimal Minimum,
        decimal Maximum);
}
