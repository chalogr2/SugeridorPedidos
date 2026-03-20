namespace PedidoSugerido.Core.Models;

public sealed record ProductHistory(
    string Code,
    string Name,
    string Model,
    decimal CurrentStock,
    int StockYear,
    IReadOnlyList<MonthlySale> Sales);
