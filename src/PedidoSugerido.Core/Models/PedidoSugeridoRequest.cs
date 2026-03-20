namespace PedidoSugerido.Core.Models;

public sealed record PedidoSugeridoRequest(
    string InputDirectory,
    string OutputPath,
    CoverageWindow Coverage,
    DateOnly DeliveryDate,
    DateOnly Today);
