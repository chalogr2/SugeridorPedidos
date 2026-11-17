using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Options;

public sealed record PedidoSugeridoCliOptions(
    string InputDirectory,
    string OutputPath,
    CoverageWindow Coverage,
    DateOnly DeliveryDate,
    DateOnly Today)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(InputDirectory) &&
        !string.IsNullOrWhiteSpace(OutputPath) &&
        Coverage.StartDate != default &&
        Coverage.EndDate != default &&
        Coverage.EndDate.DayNumber >= Coverage.StartDate.DayNumber &&
        DeliveryDate != default &&
        Today != default &&
        DeliveryDate.DayNumber >= Today.DayNumber;

    public static string Usage =>
        "Usage: PedidoSugerido.Cli --input-dir <dir> --output <file.xlsx> --coverage-start-date <yyyy-MM-dd> --coverage-end-date <yyyy-MM-dd> --delivery-date <yyyy-MM-dd> --today <yyyy-MM-dd>";

    public static PedidoSugeridoCliOptions Resolve(string[] args)
    {
        var fromArgs = FromArgs(args);
        return fromArgs.IsValid ? fromArgs : PromptForMissingValues(fromArgs);
    }

    public static PedidoSugeridoCliOptions FromArgs(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length - 1; i += 2)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                values[args[i]] = args[i + 1];
            }
        }

        var today = ParseDate(values, "--today");
        var coverage = ResolveCoverage(values, today);

        return new PedidoSugeridoCliOptions(
            values.GetValueOrDefault("--input-dir", string.Empty),
            values.GetValueOrDefault("--output", string.Empty),
            coverage,
            ParseDate(values, "--delivery-date"),
            today);
    }

    private static PedidoSugeridoCliOptions PromptForMissingValues(PedidoSugeridoCliOptions partial)
    {
        Console.WriteLine("Parametros faltantes o invalidos. Ingrese los valores requeridos.");

        var inputDirectory = PromptString("Directorio de entrada", partial.InputDirectory);
        var outputPath = PromptString(
            "Ruta de salida .xlsx",
            string.IsNullOrWhiteSpace(partial.OutputPath)
                ? Path.Combine(inputDirectory, "pedido-sugerido.xlsx")
                : partial.OutputPath);

        var todayDefault = partial.Today == default ? DateOnly.FromDateTime(DateTime.Today) : partial.Today;
        var today = PromptDate("Fecha de hoy (yyyy-MM-dd)", todayDefault);

        var coverageStartDefault = partial.Coverage.StartDate == default ? today : partial.Coverage.StartDate;
        var coverageStart = PromptDate("Fecha inicio cobertura (yyyy-MM-dd)", coverageStartDefault);
        var coverageEndDefault = partial.Coverage.EndDate == default ? coverageStart : partial.Coverage.EndDate;
        var coverageEnd = PromptDate("Fecha fin cobertura (yyyy-MM-dd)", coverageEndDefault);

        var deliveryDefault = partial.DeliveryDate == default ? today : partial.DeliveryDate;
        var deliveryDate = PromptDate("Fecha esperada de llegada (yyyy-MM-dd)", deliveryDefault);

        var resolved = new PedidoSugeridoCliOptions(
            inputDirectory,
            outputPath,
            new CoverageWindow(coverageStart, coverageEnd),
            deliveryDate,
            today);

        if (!resolved.IsValid)
        {
            throw new InvalidOperationException(Usage);
        }

        return resolved;
    }

    private static CoverageWindow ResolveCoverage(IReadOnlyDictionary<string, string> values, DateOnly today)
    {
        var explicitStart = ParseDate(values, "--coverage-start-date");
        var explicitEnd = ParseDate(values, "--coverage-end-date");
        if (explicitStart != default && explicitEnd != default)
        {
            return new CoverageWindow(
                new DateOnly(explicitStart.Year, explicitStart.Month, 1),
                new DateOnly(explicitEnd.Year, explicitEnd.Month, DateTime.DaysInMonth(explicitEnd.Year, explicitEnd.Month)));
        }

        var monthStart = ParseInt(values, "--coverage-start");
        var monthEnd = ParseInt(values, "--coverage-end");
        if (monthStart is < 0 or > 11 || monthEnd is < 0 or > 11 || today == default)
        {
            return new CoverageWindow(default, default);
        }

        var startYear = today.Year;
        var endYear = monthEnd < monthStart ? today.Year + 1 : today.Year;
        return new CoverageWindow(
            new DateOnly(startYear, monthStart + 1, 1),
            new DateOnly(endYear, monthEnd + 1, DateTime.DaysInMonth(endYear, monthEnd + 1)));
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed) ? parsed : -1;

    private static DateOnly ParseDate(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var raw) && DateOnly.TryParse(raw, out var parsed)
            ? parsed
            : default;

    private static string PromptString(string label, string currentValue)
    {
        Console.Write($"{label}{FormatDefault(currentValue)}: ");
        var input = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(input) ? currentValue : input;
    }

    private static DateOnly PromptDate(string label, DateOnly currentValue)
    {
        while (true)
        {
            Console.Write($"{label}{FormatDefault(currentValue == default ? string.Empty : currentValue.ToString("yyyy-MM-dd"))}: ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input) && currentValue != default)
            {
                return currentValue;
            }

            if (DateOnly.TryParse(input, out var parsed))
            {
                return parsed;
            }
        }
    }

    private static string FormatDefault(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $" [{value}]";
}
