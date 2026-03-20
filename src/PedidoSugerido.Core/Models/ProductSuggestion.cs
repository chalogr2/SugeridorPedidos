namespace PedidoSugerido.Core.Models;

public sealed record ProductSuggestion(
    string Code,
    string Name,
    string Model,
    decimal CurrentStock,
    ForecastMethods Methods,
    GonzalezRuizBreakdown GonzalezRuiz,
    bool IsSeasonal,
    AbcClass AbcClass,
    decimal TotalHistoricalDemand);
