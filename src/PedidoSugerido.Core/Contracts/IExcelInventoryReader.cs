using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Contracts;

public interface IExcelInventoryReader
{
    InventoryDataset ReadFromDirectory(string inputDirectory);
}
