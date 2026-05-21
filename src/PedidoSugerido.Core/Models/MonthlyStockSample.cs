namespace PedidoSugerido.Core.Models;

public sealed record MonthlyStockSample(
    int Year,
    int MonthIndex,
    decimal? Sales,
    decimal? StartStock);
