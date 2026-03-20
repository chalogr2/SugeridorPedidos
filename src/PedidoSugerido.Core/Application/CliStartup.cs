using PedidoSugerido.Core.Contracts;
using PedidoSugerido.Core.Models;
using PedidoSugerido.Core.Options;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Core.Application;

public sealed class CliStartup
{
    public int Run(string[] args)
    {
        try
        {
            var options = PedidoSugeridoCliOptions.Resolve(args);
            var request = new PedidoSugeridoRequest(
                options.InputDirectory,
                options.OutputPath,
                options.Coverage,
                options.DeliveryDate,
                options.Today);

            var application = BuildApplication();
            var result = application.Run(request);
            WriteSummary(result);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static IPedidoSugeridoApplication BuildApplication()
    {
        IExcelInventoryReader reader = new ClosedXmlInventoryReader();
        IExcelReportWriter writer = new ClosedXmlReportWriter();
        var forecastEngine = new ForecastCalculationEngine();
        return new PedidoSugeridoApplication(reader, forecastEngine, writer);
    }

    private static void WriteSummary(PedidoSugeridoResult result)
    {
        Console.WriteLine($"Archivos leídos: {result.Dataset.SourceFiles.Count}");
        Console.WriteLine($"Productos consolidados: {result.Dataset.Products.Count}");
        Console.WriteLine($"Archivo generado: {result.Request.OutputPath}");

        foreach (var product in result.Suggestions.Take(5))
        {
            Console.WriteLine(
                $"{product.Code} | {product.Name} | Stock {product.CurrentStock} | González Ruiz {product.Methods.GonzalezRuiz} | Claude {product.Methods.WeightedRecommendation}");
        }

        var sample = result.Suggestions.FirstOrDefault();
        if (sample is null)
        {
            return;
        }

        Console.WriteLine("Validación González Ruiz:");
        Console.WriteLine(
            $"{sample.Code} -> puente {sample.GonzalezRuiz.BridgeDemand}, cobertura {sample.GonzalezRuiz.CoverageDemand}, periodo {sample.GonzalezRuiz.FullPeriodDemand}, stock {sample.GonzalezRuiz.Stock}, pedido {sample.GonzalezRuiz.FinalOrder}");
    }
}
