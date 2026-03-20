namespace PedidoSugerido.Core.Services;

internal static class Statistics
{
    public static decimal CoefficientOfVariation(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var mean = values.Average();
        if (mean == 0m)
        {
            return 0m;
        }

        var variance = values
            .Select(value =>
            {
                var delta = value - mean;
                return delta * delta;
            })
            .Average();

        var standardDeviation = (decimal)Math.Sqrt((double)variance);
        return standardDeviation / mean;
    }

    public static decimal Percentile(IReadOnlyList<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0m;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var position = (sortedValues.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;
        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * fraction);
    }
}
