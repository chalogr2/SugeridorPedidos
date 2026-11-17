namespace PedidoSugerido.Core.Models;

public sealed record CoverageWindow(
    DateOnly StartDate,
    DateOnly EndDate)
{
    public int StartMonthIndex => StartDate.Month - 1;

    public int EndMonthIndex => EndDate.Month - 1;
}
