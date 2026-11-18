namespace PedidoSugerido.Core.Models;

public sealed record GonzalezRuizBreakdown(
    decimal BridgeDemand,
    decimal CoverageDemand,
    decimal FullPeriodDemand,
    decimal Stock,
    decimal FinalOrder,
    int HistoricalWindowsUsed,
    bool UsesIncompleteYears = false);
