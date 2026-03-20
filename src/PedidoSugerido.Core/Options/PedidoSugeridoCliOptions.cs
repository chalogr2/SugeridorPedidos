using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Options;

public sealed record PedidoSugeridoCliOptions(
    string InputDirectory,
    string OutputPath,
    int CoverageStartMonthIndex,
    int CoverageEndMonthIndex,
    DateOnly DeliveryDate,
    DateOnly Today)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(InputDirectory) &&
        !string.IsNullOrWhiteSpace(OutputPath) &&
        CoverageStartMonthIndex is >= 0 and <= 11 &&
        CoverageEndMonthIndex is >= 0 and <= 11 &&
        DeliveryDate != default &&
        Today != default &&
        DeliveryDate.DayNumber >= Today.DayNumber;

    public CoverageWindow Coverage => new(CoverageStartMonthIndex, CoverageEndMonthIndex);

    public static string Usage =>
        "Usage: PedidoSugerido.Cli --input-dir <dir> --output <file.xlsx> --coverage-start <0-11> --coverage-end <0-11> --delivery-date <yyyy-MM-dd> --today <yyyy-MM-dd>";

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

        return new PedidoSugeridoCliOptions(
            values.GetValueOrDefault("--input-dir", string.Empty),
            values.GetValueOrDefault("--output", string.Empty),
            ParseInt(values, "--coverage-start"),
            ParseInt(values, "--coverage-end"),
            ParseDate(values, "--delivery-date"),
            ParseDate(values, "--today"));
    }

    private static PedidoSugeridoCliOptions PromptForMissingValues(PedidoSugeridoCliOptions partial)
    {
        Console.WriteLine("Parámetros faltantes o inválidos. Ingrese los valores requeridos.");

        var inputDirectory = PromptString("Directorio de entrada", partial.InputDirectory);
        var outputPath = PromptString(
            "Ruta de salida .xlsx",
            string.IsNullOrWhiteSpace(partial.OutputPath)
                ? Path.Combine(inputDirectory, "pedido-sugerido.xlsx")
                : partial.OutputPath);

        var coverageStart = PromptMonth("Mes inicio cobertura (1-12)", partial.CoverageStartMonthIndex);
        var coverageEnd = PromptMonth("Mes fin cobertura (1-12)", partial.CoverageEndMonthIndex);
        var todayDefault = partial.Today == default ? DateOnly.FromDateTime(DateTime.Today) : partial.Today;
        var today = PromptDate("Fecha de hoy (yyyy-MM-dd)", todayDefault);
        var deliveryDefault = partial.DeliveryDate == default ? today : partial.DeliveryDate;
        var deliveryDate = PromptDate("Fecha esperada de llegada (yyyy-MM-dd)", deliveryDefault);

        var resolved = new PedidoSugeridoCliOptions(
            inputDirectory,
            outputPath,
            coverageStart,
            coverageEnd,
            deliveryDate,
            today);

        if (!resolved.IsValid)
        {
            throw new InvalidOperationException(Usage);
        }

        return resolved;
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

    private static int PromptMonth(string label, int currentValue)
    {
        while (true)
        {
            var defaultDisplay = currentValue is >= 0 and <= 11 ? (currentValue + 1).ToString() : string.Empty;
            Console.Write($"{label}{FormatDefault(defaultDisplay)}: ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input) && currentValue is >= 0 and <= 11)
            {
                return currentValue;
            }

            if (int.TryParse(input, out var monthNumber) && monthNumber is >= 1 and <= 12)
            {
                return monthNumber - 1;
            }
        }
    }

    private static DateOnly PromptDate(string label, DateOnly currentValue)
    {
        while (true)
        {
            Console.Write($"{label}{FormatDefault(currentValue.ToString("yyyy-MM-dd"))}: ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input))
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
