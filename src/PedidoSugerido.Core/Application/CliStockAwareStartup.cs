using PedidoSugerido.Core.Contracts;
using PedidoSugerido.Core.Models;
using PedidoSugerido.Core.Options;
using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Core.Application;

public sealed class CliStockAwareStartup
{
    private const string ThreeColumnFlag = "--tres-columnas";

    public int Run(string[] args)
    {
        try
        {
            var layout = args.Contains(ThreeColumnFlag, StringComparer.OrdinalIgnoreCase)
                ? StockAwareLayout.SalesMoneyAndStock
                : StockAwareLayout.SalesAndStock;
            args = args.Where(arg => !arg.Equals(ThreeColumnFlag, StringComparison.OrdinalIgnoreCase)).ToArray();

            var options = PedidoSugeridoCliOptions.Resolve(args);
            var request = new PedidoSugeridoRequest(
                options.InputDirectory,
                options.OutputPath,
                options.Coverage,
                options.DeliveryDate,
                options.Today);

            var application = BuildApplication(layout);
            var result = application.Run(request);
            WriteSummary(result, layout);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static IPedidoSugeridoApplication BuildApplication(StockAwareLayout layout)
    {
        IExcelInventoryReader reader = new ClosedXmlStockAwareInventoryReader(layout);
        IExcelReportWriter writer = new ClosedXmlReportWriter();
        var forecastEngine = new ForecastCalculationEngine();
        return new PedidoSugeridoApplication(reader, forecastEngine, writer);
    }

    private static void WriteSummary(PedidoSugeridoResult result, StockAwareLayout layout)
    {
        Console.WriteLine($"Archivos leidos: {result.Dataset.SourceFiles.Count}");
        Console.WriteLine($"Productos consolidados: {result.Dataset.Products.Count}");
        Console.WriteLine($"Archivo generado: {result.Request.OutputPath}");
        Console.WriteLine("Modo: stock-aware (meses sin stock imputados a partir de meses con stock).");
        Console.WriteLine(layout == StockAwareLayout.SalesMoneyAndStock
            ? "Formato: 3 columnas por mes (unidades, venta sin IVA, stock inicial)."
            : "Formato: 2 columnas por mes (unidades, stock inicial).");

        foreach (var product in result.Suggestions.Take(5))
        {
            Console.WriteLine(
                $"{product.Code} | {product.Name} | Stock {product.CurrentStock} | Gonzalez Ruiz {product.Methods.GonzalezRuiz} | Claude {product.Methods.WeightedRecommendation}");
        }

        var sample = result.Suggestions.FirstOrDefault();
        if (sample is null)
        {
            return;
        }

        Console.WriteLine("Validacion Gonzalez Ruiz:");
        Console.WriteLine(
            $"{sample.Code} -> puente {sample.GonzalezRuiz.BridgeDemand}, cobertura {sample.GonzalezRuiz.CoverageDemand}, periodo {sample.GonzalezRuiz.FullPeriodDemand}, stock {sample.GonzalezRuiz.Stock}, pedido {sample.GonzalezRuiz.FinalOrder}");
    }
}
