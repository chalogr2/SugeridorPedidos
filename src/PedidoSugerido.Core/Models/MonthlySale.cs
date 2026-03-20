namespace PedidoSugerido.Core.Models;

public sealed record MonthlySale(
    int Year,
    int MonthIndex,
    decimal Value);
