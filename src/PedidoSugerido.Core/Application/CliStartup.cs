using PedidoSugerido.Core.Contracts;
using PedidoSugerido.Core.Models;
using PedidoSugerido.Core.Options;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Core.Application;

public sealed class CliStartup
{
    public int Run(string[] args)
    {
        if (PedidoSugeridoCliOptions.IsHelpRequested(args))
        {
            Console.WriteLine(PedidoSugeridoCliOptions.Usage);
            return 0;
        }

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
        Console.WriteLine($"Archivos leidos: {result.Dataset.SourceFiles.Count}");
        Console.WriteLine($"Productos consolidados: {result.Dataset.Products.Count}");
        Console.WriteLine($"Archivo generado: {result.Request.OutputPath}");

        foreach (var product in result.Suggestions.Take(5))
        {
            Console.WriteLine(
                $"{product.Code} | {product.Name} | Stock {product.CurrentStock} | Gonzalez Ruiz {product.Methods.GonzalezRuiz} | GR per-month {product.Methods.GonzalezRuizPerMonth} | Claude {product.Methods.WeightedRecommendation}");
        }

        var sample = result.Suggestions.FirstOrDefault();
        if (sample is null)
        {
            return;
        }

        Console.WriteLine("Validacion Gonzalez Ruiz:");
        Console.WriteLine(
            $"{sample.Code} -> puente {sample.GonzalezRuiz.BridgeDemand}, cobertura {sample.GonzalezRuiz.CoverageDemand}, periodo {sample.GonzalezRuiz.FullPeriodDemand}, stock {sample.GonzalezRuiz.Stock}, pedido {sample.GonzalezRuiz.FinalOrder}, años {sample.GonzalezRuiz.HistoricalWindowsUsed}");

        var incomplete = result.Suggestions.Count(x => x.GonzalezRuiz.UsesIncompleteYears);
        if (incomplete > 0)
        {
            Console.WriteLine($"Aviso: {incomplete} producto(s) sin ningun año completo; Gonzalez Ruiz usa años parciales (meses faltantes cuentan como 0).");
        }
    }
}
