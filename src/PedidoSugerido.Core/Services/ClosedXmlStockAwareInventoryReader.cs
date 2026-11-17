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

    private static readonly Regex YearRegex = new(@"(19|20)\d{2}", RegexOptions.Compiled);

    private readonly StockoutImputationService _imputation;
    private readonly StockAwareLayout _layout;

    public ClosedXmlStockAwareInventoryReader()
        : this(new StockoutImputationService(), StockAwareLayout.SalesAndStock)
    {
    }

    public ClosedXmlStockAwareInventoryReader(StockAwareLayout layout)
        : this(new StockoutImputationService(), layout)
    {
    }

    public ClosedXmlStockAwareInventoryReader(StockoutImputationService imputation)
        : this(imputation, StockAwareLayout.SalesAndStock)
    {
    }

    public ClosedXmlStockAwareInventoryReader(StockoutImputationService imputation, StockAwareLayout layout)
    {
        _imputation = imputation;
        _layout = layout;
    }

    private int ColumnsPerMonth => _layout == StockAwareLayout.SalesMoneyAndStock ? 3 : 2;

    private int SalesColumn(int monthIndex) => FirstMonthColumn + (monthIndex * ColumnsPerMonth);

    private int StartStockColumn(int monthIndex) => SalesColumn(monthIndex) + ColumnsPerMonth - 1;

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

        var readFiles = new List<string>();

        foreach (var file in files)
        {
            using var workbook = new XLWorkbook(file);
            if (IsPedidoOutput(workbook))
            {
                continue;
            }

            readFiles.Add(file);
            var worksheet = SelectDataWorksheet(workbook)
                ?? throw new InvalidOperationException($"No data worksheet found in '{file}'.");

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var headers = Enumerable.Range(1, lastColumn)
                .Select(column => worksheet.Cell(1, column).GetString().Trim())
                .ToList();
            ValidateLayout(headers, worksheet.Name, file);
            var year = ParseYear(headers, file);

            for (var row = 2; row <= lastRow; row++)
            {
                var code = worksheet.Cell(row, CodeColumn).GetString().Trim();
                if (string.IsNullOrWhiteSpace(code) || code.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
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
                    var sales = ParseNumericCell(worksheet.Cell(row, SalesColumn(monthIndex)));
                    var startStock = ParseNumericCell(worksheet.Cell(row, StartStockColumn(monthIndex)));
                    aggregate.AddSample(new MonthlyStockSample(year, monthIndex, sales, startStock));
                }
            }
        }

        var products = aggregators
            .Values
            .Select(x => x.ToProductHistory(_imputation))
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (readFiles.Count == 0)
        {
            throw new InvalidOperationException($"No sales .xlsx files were found in '{inputDirectory}'.");
        }

        return new InventoryDataset(products, readFiles);
    }

    // Previous pedido outputs often sit next to the sales files; they always start with this sheet.
    private static bool IsPedidoOutput(XLWorkbook workbook) =>
        workbook.Worksheets.First().Name.Equals("Todos los Productos", StringComparison.OrdinalIgnoreCase);

    private static IXLWorksheet? SelectDataWorksheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            !sheet.Name.Equals("Control", StringComparison.OrdinalIgnoreCase) &&
            sheet.Name.IndexOf("Exclu", StringComparison.OrdinalIgnoreCase) < 0);

    // Months are read by position, so a file in the other layout would silently mix money or
    // stock balances into the sales series. Every month's start-stock column must say "stock"
    // and its sales column must not.
    private void ValidateLayout(IReadOnlyList<string> headers, string worksheetName, string filePath)
    {
        string Header(int column) => column <= headers.Count ? headers[column - 1] : string.Empty;

        var matches = Enumerable.Range(0, 12).All(month =>
            Header(StartStockColumn(month)).Contains("stock", StringComparison.OrdinalIgnoreCase) &&
            !Header(SalesColumn(month)).Contains("stock", StringComparison.OrdinalIgnoreCase));
        if (matches)
        {
            return;
        }

        var hint = _layout == StockAwareLayout.SalesMoneyAndStock
            ? "Expected 3 columns per month (units, sales without IVA, start stock). If the file has 2 columns per month, remove --tres-columnas."
            : "Expected 2 columns per month (units, start stock). If the file has 3 columns per month (with sales without IVA), add --tres-columnas.";
        throw new InvalidOperationException(
            $"Worksheet '{worksheetName}' in '{filePath}' does not match the expected layout. {hint}");
    }

    private static int ParseYear(IReadOnlyList<string> headers, string filePath)
    {
        var totalHeaderYear = headers
            .Where(header => header.StartsWith("Total", StringComparison.OrdinalIgnoreCase))
            .Select(ParseFirstYear)
            .FirstOrDefault(year => year.HasValue);
        if (totalHeaderYear.HasValue)
        {
            return totalHeaderYear.Value;
        }

        var fileYear = ParseFirstYear(Path.GetFileNameWithoutExtension(filePath));
        if (fileYear.HasValue)
        {
            return fileYear.Value;
        }

        throw new InvalidOperationException(
            $"Could not determine year from the Total header or file '{filePath}'.");
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
