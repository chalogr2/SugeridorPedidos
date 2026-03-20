namespace PedidoSugerido.Core.Services;

internal static class MonthHelper
{
    public static IReadOnlyList<int> GetInclusiveMonthSequence(int startMonthIndex, int endMonthIndex)
    {
        var months = new List<int>();
        var current = startMonthIndex;

        while (true)
        {
            months.Add(current);
            if (current == endMonthIndex)
            {
                break;
            }

            current = (current + 1) % 12;
        }

        return months;
    }

    public static IReadOnlyList<int> GetBridgeMonths(int currentMonthIndex, int coverageStartMonthIndex)
    {
        if (currentMonthIndex == coverageStartMonthIndex)
        {
            return Array.Empty<int>();
        }

        var months = new List<int>();
        var month = currentMonthIndex;

        while (month != coverageStartMonthIndex)
        {
            months.Add(month);
            month = (month + 1) % 12;
        }

        return months;
    }

    public static IReadOnlyList<PeriodMonth> GetPeriodSequence(int currentMonthIndex, int coverageEndMonthIndex)
    {
        var result = new List<PeriodMonth>();
        var month = currentMonthIndex;
        var yearOffset = 0;

        while (true)
        {
            result.Add(new PeriodMonth(month, yearOffset));
            if (month == coverageEndMonthIndex)
            {
                break;
            }

            month = (month + 1) % 12;
            if (month == 0)
            {
                yearOffset++;
            }
        }

        return result;
    }

    internal sealed record PeriodMonth(int MonthIndex, int YearOffset);
}
