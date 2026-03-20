using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Contracts;

public interface IPedidoSugeridoApplication
{
    PedidoSugeridoResult Run(PedidoSugeridoRequest request);
}
