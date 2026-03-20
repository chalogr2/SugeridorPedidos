namespace PedidoSugerido.Core.Models;

public sealed record ForecastMethods(
    decimal GonzalezRuiz,
    decimal Average,
    decimal Minimum,
    decimal Maximum,
    decimal ExponentialSmoothing,
    decimal SeasonalComparable,
    decimal WeightedRecommendation);
