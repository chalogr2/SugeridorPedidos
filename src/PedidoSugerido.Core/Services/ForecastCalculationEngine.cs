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

        var coveragePeriod = BuildMonthSequence(
            new DateOnly(request.Coverage.StartDate.Year, request.Coverage.StartDate.Month, 1),
            new DateOnly(request.Coverage.EndDate.Year, request.Coverage.EndDate.Month, 1));

        var bridgePeriod = BuildBridgeSequence(request.Today, request.Coverage.StartDate);
        var coverageMonths = coveragePeriod.Select(x => x.MonthIndex).ToList();

        var average = allValues.Count == 0 ? 0m : allValues.Average();
        var minimum = allValues.Count == 0 ? 0m : allValues.Min();
        var maximum = allValues.Count == 0 ? 0m : allValues.Max();
        var exponential = CalculateExponentialSmoothing(allValues);
        var seasonalCoverageDemand = CalculateSeasonalCoverageDemand(orderedSales, coverageMonths);
        var gonzalez = CalculateGonzalezRuiz(product, bridgePeriod, coveragePeriod);
        var gonzalezPerMonth = CalculateGonzalezRuizPerMonth(product, bridgePeriod, coveragePeriod);
        var totalDemand = allValues.Sum();
        var isSeasonal = Statistics.CoefficientOfVariation(allValues) > 0.8m;

        return new ProductComputationContext(
            product,
            leadTimeMonths,
            coveragePeriod,
            average,
            minimum,
            maximum,
            exponential,
            seasonalCoverageDemand,
            gonzalez,
            gonzalezPerMonth,
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
                weightedOrder,
                context.GonzalezRuizPerMonth),
            context.GonzalezRuiz,
            context.IsSeasonal,
            abcClass,
            context.TotalHistoricalDemand);
    }

    private static GonzalezRuizBreakdown CalculateGonzalezRuiz(
        ProductHistory product,
        IReadOnlyList<HistoricalMonth> bridgeMonths,
        IReadOnlyList<HistoricalMonth> coverageMonths)
    {
        var salesLookup = product.Sales.ToDictionary(x => (x.Year, x.MonthIndex), x => x.Value);
        var years = product.Sales.Select(x => x.Year).Distinct().OrderBy(x => x).ToList();
        var periodMonths = bridgeMonths.Concat(coverageMonths).ToList();
        var completeWindows = new List<(decimal Bridge, decimal Coverage, decimal Full, decimal Order)>();
        var partialWindows = new List<(decimal Bridge, decimal Coverage, decimal Full, decimal Order)>();

        foreach (var anchorYear in years)
        {
            var hasCoverage = coverageMonths.Any(month => salesLookup.ContainsKey((anchorYear, month.MonthIndex)));
            if (!hasCoverage)
            {
                continue;
            }

            var bridgeDemand = bridgeMonths.Sum(month => salesLookup.GetValueOrDefault((anchorYear, month.MonthIndex), 0m));
            var coverageDemand = coverageMonths.Sum(month => salesLookup.GetValueOrDefault((anchorYear, month.MonthIndex), 0m));
            var full = periodMonths.Sum(month => salesLookup.GetValueOrDefault((anchorYear, month.MonthIndex), 0m));
            var window = (bridgeDemand, coverageDemand, full, Math.Max(0m, full - product.CurrentStock));

            // A missing month would count as zero demand, so a partial year (e.g. the current
            // year to date) would drag the average down. Only complete years are used, unless
            // the product has none (new products), where partial years are better than nothing.
            var isComplete = periodMonths.All(month => salesLookup.ContainsKey((anchorYear, month.MonthIndex)));
            (isComplete ? completeWindows : partialWindows).Add(window);
        }

        var usesIncompleteYears = completeWindows.Count == 0 && partialWindows.Count > 0;
        var windowResults = usesIncompleteYears ? partialWindows : completeWindows;

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
            windowResults.Count,
            usesIncompleteYears);
    }

    // Same period as González Ruiz (bridge + coverage, a month counted as often as the period
    // contains it), but each calendar month's demand is the average of that month over every
    // year that has it. Partial years (e.g. the current year to date) contribute their real
    // months, and a missing month is never counted as zero. A month with no data in any year
    // falls back to the product's overall monthly average. Stock is subtracted once.
    private static decimal CalculateGonzalezRuizPerMonth(
        ProductHistory product,
        IReadOnlyList<HistoricalMonth> bridgeMonths,
        IReadOnlyList<HistoricalMonth> coverageMonths)
    {
        if (product.Sales.Count == 0)
        {
            return 0m;
        }

        var monthAverages = product.Sales
            .GroupBy(x => x.MonthIndex)
            .ToDictionary(g => g.Key, g => g.Average(x => x.Value));
        var overallAverage = product.Sales.Average(x => x.Value);

        var periodDemand = bridgeMonths
            .Concat(coverageMonths)
            .Sum(month => monthAverages.TryGetValue(month.MonthIndex, out var average) ? average : overallAverage);

        return ClampToOrder(periodDemand - product.CurrentStock);
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
        var allValues = sales.Select(x => x.Value).ToList();
        var globalAverage = allValues.Count == 0 ? 0m : allValues.Average();
        decimal total = 0m;
        foreach (var month in coverageMonths)
        {
            var monthValues = sales.Where(x => x.MonthIndex == month).Select(x => x.Value).ToList();
            total += monthValues.Count == 0 ? globalAverage : monthValues.Average();
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

    private static IReadOnlyList<HistoricalMonth> BuildMonthSequence(DateOnly startMonth, DateOnly endMonth)
    {
        var months = new List<HistoricalMonth>();
        var current = startMonth;
        var yearOffset = 0;

        while (true)
        {
            months.Add(new HistoricalMonth(current.Month - 1, yearOffset));
            if (current.Year == endMonth.Year && current.Month == endMonth.Month)
            {
                break;
            }

            current = current.AddMonths(1);
            yearOffset = current.Year - startMonth.Year;
        }

        return months;
    }

    private static IReadOnlyList<HistoricalMonth> BuildBridgeSequence(DateOnly today, DateOnly coverageStartDate)
    {
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var coverageStartMonth = new DateOnly(coverageStartDate.Year, coverageStartDate.Month, 1);

        if (coverageStartMonth <= currentMonthStart)
        {
            return Array.Empty<HistoricalMonth>();
        }

        var bridgeEnd = coverageStartMonth.AddMonths(-1);
        return BuildMonthSequence(currentMonthStart, bridgeEnd);
    }

    private sealed record ProductComputationContext(
        ProductHistory Product,
        decimal LeadTimeMonths,
        IReadOnlyList<HistoricalMonth> CoverageMonths,
        decimal AverageDemand,
        decimal MinimumDemand,
        decimal MaximumDemand,
        decimal ExponentialSmoothingDemand,
        decimal SeasonalCoverageDemand,
        GonzalezRuizBreakdown GonzalezRuiz,
        decimal GonzalezRuizPerMonth,
        decimal TotalHistoricalDemand,
        bool IsSeasonal);

    private sealed record HistoricalMonth(int MonthIndex, int YearOffset);

    private sealed record WeightProfile(
        decimal GonzalezRuiz,
        decimal SeasonalComparable,
        decimal ExponentialSmoothing,
        decimal Average,
        decimal Minimum,
        decimal Maximum);
}
