using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PedidoSugerido.Core.Contracts;
using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Services;

public sealed class ClosedXmlStockAwareInventoryReader : IExcelInventoryReader
{
    private const int CodeColumn = 1;
    private const int ModelColumn = 2;
    private const int NameColumn = 3;
    private const int StockColumn = 4;
    private const int FirstMonthColumn = 5;
    private const int TotalAnualColumn = 29;
    private const int MinRequiredColumns = 28;

    private static readonly Regex YearRegex = new(@"(19|20)\d{2}", RegexOptions.Compiled);

    private readonly StockoutImputationService _imputation;

    public ClosedXmlStockAwareInventoryReader()
        : this(new StockoutImputationService())
    {
    }

    public ClosedXmlStockAwareInventoryReader(StockoutImputationService imputation)
    {
        _imputation = imputation;
    }

    public InventoryDataset ReadFromDirectory(string inputDirectory)
    {
        if (!Directory.Exists(inputDirectory))
        {
            throw new DirectoryNotFoundException($"Input directory not found: {inputDirectory}");
        }

        var files = Directory
            .EnumerateFiles(inputDirectory, "*.xlsx", SearchOption.TopDirectoryOnly)
            .Where(file => !ShouldIgnoreFile(file))
            .OrderBy(ParseYearFromPath)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No .xlsx files were found in '{inputDirectory}'.");
        }

        var aggregators = new Dictionary<string, StockAwareProductAggregate>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            using var workbook = new XLWorkbook(file);
            var worksheet = SelectDataWorksheet(workbook)
                ?? throw new InvalidOperationException($"No data worksheet found in '{file}'.");

            var year = ParseYear(worksheet, file);
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

            if (lastColumn < MinRequiredColumns)
            {
                throw new InvalidOperationException(
                    $"Worksheet '{worksheet.Name}' in '{file}' must have at least {MinRequiredColumns} columns for the stock-aware format.");
            }

            for (var row = 2; row <= lastRow; row++)
            {
                var code = worksheet.Cell(row, CodeColumn).GetString().Trim();
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                var model = worksheet.Cell(row, ModelColumn).GetString().Trim();
                var name = worksheet.Cell(row, NameColumn).GetString().Trim();
                var stock = ParseNumericCell(worksheet.Cell(row, StockColumn)) ?? 0m;

                if (!aggregators.TryGetValue(code, out var aggregate))
                {
                    aggregate = new StockAwareProductAggregate(code);
                    aggregators.Add(code, aggregate);
                }

                aggregate.UpdateIdentity(name, model);
                aggregate.UpdateStock(year, stock, name, model);

                for (var monthIndex = 0; monthIndex < 12; monthIndex++)
                {
                    var salesColumn = FirstMonthColumn + (monthIndex * 2);
                    var stockColumnForMonth = salesColumn + 1;
                    var sales = ParseNumericCell(worksheet.Cell(row, salesColumn));
                    var startStock = ParseNumericCell(worksheet.Cell(row, stockColumnForMonth));
                    aggregate.AddSample(new MonthlyStockSample(year, monthIndex, sales, startStock));
                }
            }
        }

        var products = aggregators
            .Values
            .Select(x => x.ToProductHistory(_imputation))
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InventoryDataset(products, files);
    }

    private static IXLWorksheet? SelectDataWorksheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            !sheet.Name.Equals("Control", StringComparison.OrdinalIgnoreCase) &&
            sheet.Name.IndexOf("Exclu", StringComparison.OrdinalIgnoreCase) < 0);

    private static int ParseYear(IXLWorksheet worksheet, string filePath)
    {
        var header = worksheet.Cell(1, TotalAnualColumn).GetString().Trim();
        var headerYear = ParseFirstYear(header);
        if (headerYear.HasValue)
        {
            return headerYear.Value;
        }

        var fileYear = ParseFirstYear(Path.GetFileNameWithoutExtension(filePath));
        if (fileYear.HasValue)
        {
            return fileYear.Value;
        }

        throw new InvalidOperationException(
            $"Could not determine year from worksheet '{worksheet.Name}' or file '{filePath}'.");
    }

    private static int ParseYearFromPath(string filePath) =>
        ParseFirstYear(Path.GetFileNameWithoutExtension(filePath)) ?? int.MinValue;

    private static bool ShouldIgnoreFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.StartsWith("~", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("pedido", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseFirstYear(string value)
    {
        var match = YearRegex.Match(value);
        return match.Success && int.TryParse(match.Value, out var year) ? year : null;
    }

    private static decimal? ParseNumericCell(IXLCell cell)
    {
        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("no data", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (cell.TryGetValue<decimal>(out var decimalValue))
        {
            return decimal.Truncate(decimalValue);
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimalValue))
        {
            return decimal.Truncate(decimalValue);
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out decimalValue))
        {
            return decimal.Truncate(decimalValue);
        }

        throw new InvalidOperationException($"Cell '{cell.Address}' contains a non-numeric value: '{text}'.");
    }

    private sealed class StockAwareProductAggregate
    {
        private readonly Dictionary<(int Year, int MonthIndex), MonthlyStockSample> _samples = new();

        public StockAwareProductAggregate(string code)
        {
            Code = code;
            Name = string.Empty;
            Model = string.Empty;
            CurrentStock = 0m;
            StockYear = int.MinValue;
        }

        public string Code { get; }

        public string Name { get; private set; }

        public string Model { get; private set; }

        public decimal CurrentStock { get; private set; }

        public int StockYear { get; private set; }

        public void UpdateIdentity(string name, string model)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Name = name;
            }

            if (!string.IsNullOrWhiteSpace(model))
            {
                Model = model;
            }
        }

        public void UpdateStock(int year, decimal stock, string name, string model)
        {
            if (year < StockYear)
            {
                return;
            }

            StockYear = year;
            CurrentStock = stock;

            if (!string.IsNullOrWhiteSpace(name))
            {
                Name = name;
            }

            if (!string.IsNullOrWhiteSpace(model))
            {
                Model = model;
            }
        }

        public void AddSample(MonthlyStockSample sample) =>
            _samples[(sample.Year, sample.MonthIndex)] = sample;

        public ProductHistory ToProductHistory(StockoutImputationService imputation)
        {
            var ordered = _samples
                .OrderBy(x => x.Key.Year)
                .ThenBy(x => x.Key.MonthIndex)
                .Select(x => x.Value)
                .ToList();

            var sales = imputation.Impute(ordered);
            return new ProductHistory(Code, Name, Model, CurrentStock, StockYear, sales);
        }
    }
}
