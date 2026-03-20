using PedidoSugerido.Core.Contracts;
using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Services;

public sealed class PedidoSugeridoApplication : IPedidoSugeridoApplication
{
    private readonly IExcelInventoryReader _reader;
    private readonly ForecastCalculationEngine _forecastEngine;
    private readonly IExcelReportWriter _writer;

    public PedidoSugeridoApplication(
        IExcelInventoryReader reader,
        ForecastCalculationEngine forecastEngine,
        IExcelReportWriter writer)
    {
        _reader = reader;
        _forecastEngine = forecastEngine;
        _writer = writer;
    }

    public PedidoSugeridoResult Run(PedidoSugeridoRequest request)
    {
        var dataset = _reader.ReadFromDirectory(request.InputDirectory);
        var suggestions = _forecastEngine.Calculate(dataset.Products, request);
        _writer.Write(request.OutputPath, suggestions);

        return new PedidoSugeridoResult(request, dataset, suggestions);
    }
}
