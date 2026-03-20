namespace PedidoSugerido.Core.Models;

public sealed record InventoryDataset(
    IReadOnlyList<ProductHistory> Products,
    IReadOnlyList<string> SourceFiles);
