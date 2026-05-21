using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Services;

public sealed class StockoutImputationService
{
    public IReadOnlyList<MonthlySale> Impute(IReadOnlyList<MonthlyStockSample> samples)
    {
        var inStockPool = samples
            .Where(s => s.StartStock is > 0m && s.Sales is not null)
            .Select(s => (s.MonthIndex, Value: s.Sales!.Value))
            .ToList();

        var globalPool = inStockPool.Select(x => x.Value).ToList();
        var sameMonthLookup = inStockPool
            .GroupBy(x => x.MonthIndex)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value).ToList());

        var globalFallback = globalPool.Count == 0 ? 0m : Median(globalPool);
        var output = new List<MonthlySale>(samples.Count);

        foreach (var sample in samples)
        {
            if (IsStockout(sample))
            {
                var imputed = sameMonthLookup.TryGetValue(sample.MonthIndex, out var pool) && pool.Count > 0
                    ? Median(pool)
                    : globalFallback;
                output.Add(new MonthlySale(sample.Year, sample.MonthIndex, imputed));
                continue;
            }

            if (sample.Sales is null)
            {
                continue;
            }

            output.Add(new MonthlySale(sample.Year, sample.MonthIndex, sample.Sales.Value));
        }

        return output;
    }

    private static bool IsStockout(MonthlyStockSample sample)
    {
        if (sample.StartStock is not 0m)
        {
            return false;
        }

        return sample.Sales is null || sample.Sales.Value == 0m;
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2m
            : sorted[middle];
    }
}
