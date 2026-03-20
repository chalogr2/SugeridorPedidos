namespace PedidoSugerido.Core.Models;

public sealed record PedidoSugeridoResult(
    PedidoSugeridoRequest Request,
    InventoryDataset Dataset,
    IReadOnlyList<ProductSuggestion> Suggestions);
