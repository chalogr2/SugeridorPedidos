using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Contracts;

public interface IExcelReportWriter
{
    void Write(string outputPath, IReadOnlyCollection<ProductSuggestion> suggestions);
}
