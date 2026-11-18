using System.Globalization;
using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Options;

public sealed record PedidoSugeridoCliOptions(
    string InputDirectory,
    string OutputPath,
    CoverageWindow Coverage,
    DateOnly DeliveryDate,
    DateOnly Today)
{
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly HashSet<string> ValueFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--input-dir",
        "--output",
        "--coverage-start-date",
        "--coverage-end-date",
        "--coverage-start",
        "--coverage-end",
        "--delivery-date",
        "--today"
    };

    public bool IsValid => MissingValues().Count == 0 && InvalidValues().Count == 0;

    public static string Usage => UsageFor("PedidoSugerido.Cli");

    public static string UsageFor(string programName, params string[] extraOptions)
    {
        var lines = new List<string>
        {
            $"Usage: {programName} --input-dir <dir> --output <file.xlsx> --coverage-start-date <yyyy-MM-dd> --coverage-end-date <yyyy-MM-dd> --delivery-date <yyyy-MM-dd> --today <yyyy-MM-dd>",
            "",
            "  --input-dir             Folder with the yearly sales .xlsx files",
            "  --output                Report to create (.xlsx). An existing file is only replaced if it is a previous pedido report",
            "  --coverage-start-date   First month to cover (yyyy-MM-dd; the day is ignored)",
            "  --coverage-end-date     Last month to cover (yyyy-MM-dd; the day is ignored)",
            "  --coverage-start/-end   Legacy alternative: 0-based month indexes (0 = January), relative to --today",
            "  --delivery-date         Expected arrival date of the order (yyyy-MM-dd)",
            "  --today                 Reference date for the calculation (yyyy-MM-dd)"
        };
        lines.AddRange(extraOptions.Select(option => "  " + option));
        lines.Add("  --help                  Show this help");
        lines.Add("");
        lines.Add("Run with no arguments in a terminal to be prompted for each value.");
        return string.Join(Environment.NewLine, lines);
    }

    public static bool IsHelpRequested(IEnumerable<string> args) =>
        args.Any(arg => arg is "--help" or "-h" or "/?");

    public static PedidoSugeridoCliOptions Resolve(string[] args) =>
        Resolve(args, Console.IsInputRedirected);

    // Values that were given but are wrong always stop the run. Values that are missing are
    // prompted for only when someone can answer; otherwise the run stops instead of guessing.
    public static PedidoSugeridoCliOptions Resolve(string[] args, bool inputRedirected)
    {
        var (options, errors) = Parse(args);
        errors.AddRange(options.InvalidValues());
        if (errors.Count > 0)
        {
            throw new ArgumentException(FormatErrors(errors));
        }

        var missing = options.MissingValues();
        if (missing.Count == 0)
        {
            return options;
        }

        if (inputRedirected)
        {
            throw new ArgumentException(FormatErrors(missing.Select(value => $"Missing {value}.")));
        }

        return PromptForMissingValues(options);
    }

    public static PedidoSugeridoCliOptions FromArgs(string[] args)
    {
        var (options, errors) = Parse(args);
        if (errors.Count > 0)
        {
            throw new ArgumentException(FormatErrors(errors));
        }

        return options;
    }

    private IReadOnlyList<string> MissingValues()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(InputDirectory)) missing.Add("--input-dir");
        if (string.IsNullOrWhiteSpace(OutputPath)) missing.Add("--output");
        if (Coverage.StartDate == default || Coverage.EndDate == default) missing.Add("coverage (--coverage-start-date and --coverage-end-date)");
        if (DeliveryDate == default) missing.Add("--delivery-date");
        if (Today == default) missing.Add("--today");
        return missing;
    }

    private IReadOnlyList<string> InvalidValues()
    {
        var invalid = new List<string>();
        if (!string.IsNullOrWhiteSpace(OutputPath) &&
            !Path.GetExtension(OutputPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            invalid.Add($"--output must be an .xlsx file: '{OutputPath}'.");
        }

        if (Coverage.StartDate != default && Coverage.EndDate != default && Coverage.EndDate < Coverage.StartDate)
        {
            invalid.Add($"Coverage end ({Coverage.EndDate:yyyy-MM}) is before coverage start ({Coverage.StartDate:yyyy-MM}).");
        }

        if (DeliveryDate != default && Today != default && DeliveryDate < Today)
        {
            invalid.Add($"--delivery-date ({DeliveryDate:yyyy-MM-dd}) is before --today ({Today:yyyy-MM-dd}).");
        }

        return invalid;
    }

    private static (PedidoSugeridoCliOptions Options, List<string> Errors) Parse(string[] args)
    {
        var errors = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (!ValueFlags.Contains(flag))
            {
                errors.Add($"Unknown argument '{flag}'.");
                continue;
            }

            if (i + 1 >= args.Length || ValueFlags.Contains(args[i + 1]))
            {
                errors.Add($"Missing value for {flag}.");
                continue;
            }

            var value = args[++i];
            if (!values.TryAdd(flag, value))
            {
                errors.Add($"{flag} was given more than once.");
            }
        }

        var today = ParseDate(values, "--today", errors);
        var coverage = ParseCoverage(values, today, errors);

        var options = new PedidoSugeridoCliOptions(
            values.GetValueOrDefault("--input-dir", string.Empty),
            values.GetValueOrDefault("--output", string.Empty),
            coverage,
            ParseDate(values, "--delivery-date", errors),
            today);

        return (options, errors);
    }

    private static CoverageWindow ParseCoverage(IReadOnlyDictionary<string, string> values, DateOnly today, List<string> errors)
    {
        var hasDates = values.ContainsKey("--coverage-start-date") || values.ContainsKey("--coverage-end-date");
        var hasMonths = values.ContainsKey("--coverage-start") || values.ContainsKey("--coverage-end");

        if (hasDates && hasMonths)
        {
            errors.Add("Use either --coverage-start-date/--coverage-end-date or the legacy --coverage-start/--coverage-end, not both.");
            return new CoverageWindow(default, default);
        }

        if (hasDates)
        {
            var start = ParseDate(values, "--coverage-start-date", errors);
            var end = ParseDate(values, "--coverage-end-date", errors);
            if (!values.ContainsKey("--coverage-start-date") || !values.ContainsKey("--coverage-end-date"))
            {
                errors.Add("--coverage-start-date and --coverage-end-date must be given together.");
            }

            return start == default || end == default
                ? new CoverageWindow(default, default)
                : MonthWindow(start, end);
        }

        if (hasMonths)
        {
            var monthStart = ParseMonthIndex(values, "--coverage-start", errors);
            var monthEnd = ParseMonthIndex(values, "--coverage-end", errors);
            if (!values.ContainsKey("--coverage-start") || !values.ContainsKey("--coverage-end"))
            {
                errors.Add("--coverage-start and --coverage-end must be given together.");
            }

            if (!values.ContainsKey("--today"))
            {
                errors.Add("--coverage-start/--coverage-end need --today to know which year they refer to.");
            }

            if (monthStart is null || monthEnd is null || today == default)
            {
                return new CoverageWindow(default, default);
            }

            var startYear = today.Year;
            var endYear = monthEnd < monthStart ? today.Year + 1 : today.Year;
            return MonthWindow(new DateOnly(startYear, monthStart.Value + 1, 1), new DateOnly(endYear, monthEnd.Value + 1, 1));
        }

        return new CoverageWindow(default, default);
    }

    private static CoverageWindow MonthWindow(DateOnly start, DateOnly end) =>
        new(
            new DateOnly(start.Year, start.Month, 1),
            new DateOnly(end.Year, end.Month, DateTime.DaysInMonth(end.Year, end.Month)));

    private static int? ParseMonthIndex(IReadOnlyDictionary<string, string> values, string key, List<string> errors)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return null;
        }

        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var month) && month is >= 0 and <= 11)
        {
            return month;
        }

        errors.Add($"Invalid value for {key}: '{raw}' (expected a month index from 0 = January to 11 = December).");
        return null;
    }

    private static DateOnly ParseDate(IReadOnlyDictionary<string, string> values, string key, List<string> errors)
    {
        if (!values.TryGetValue(key, out var raw))
        {
            return default;
        }

        if (TryParseDate(raw, out var parsed))
        {
            return parsed;
        }

        errors.Add($"Invalid date for {key}: '{raw}' (expected {DateFormat}).");
        return default;
    }

    private static bool TryParseDate(string? raw, out DateOnly parsed) =>
        DateOnly.TryParseExact(raw?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

    private static string FormatErrors(IEnumerable<string> errors) =>
        string.Join(Environment.NewLine, errors) + Environment.NewLine + "Run with --help to see the usage.";

    private static PedidoSugeridoCliOptions PromptForMissingValues(PedidoSugeridoCliOptions partial)
    {
        Console.WriteLine("Parametros faltantes. Ingrese los valores requeridos.");

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
            MonthWindow(coverageStart, coverageEnd),
            deliveryDate,
            today);

        var problems = resolved.MissingValues().Select(value => $"Missing {value}.")
            .Concat(resolved.InvalidValues())
            .ToList();
        if (problems.Count > 0)
        {
            throw new ArgumentException(FormatErrors(problems));
        }

        return resolved;
    }

    private static string ReadAnswer() =>
        Console.ReadLine()?.Trim()
        ?? throw new ArgumentException("Input ended before all values were given. Pass them as arguments instead (see --help).");

    private static string PromptString(string label, string currentValue)
    {
        Console.Write($"{label}{FormatDefault(currentValue)}: ");
        var input = ReadAnswer();
        return string.IsNullOrWhiteSpace(input) ? currentValue : input;
    }

    private static DateOnly PromptDate(string label, DateOnly currentValue)
    {
        while (true)
        {
            Console.Write($"{label}{FormatDefault(currentValue == default ? string.Empty : currentValue.ToString(DateFormat))}: ");
            var input = ReadAnswer();
            if (string.IsNullOrWhiteSpace(input) && currentValue != default)
            {
                return currentValue;
            }

            if (TryParseDate(input, out var parsed))
            {
                return parsed;
            }

            Console.WriteLine($"Fecha invalida, use el formato {DateFormat}.");
        }
    }

    private static string FormatDefault(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $" [{value}]";
}
