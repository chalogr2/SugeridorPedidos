using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PedidoSugerido.Core.Contracts;
using PedidoSugerido.Core.Models;

namespace PedidoSugerido.Core.Services;

public sealed class ClosedXmlInventoryReader : IExcelInventoryReader
{
    private const int StockColumn = 4;
    private const int FirstMonthColumn = 5;

    private static readonly Regex YearRegex = new(@"(19|20)\d{2}", RegexOptions.Compiled);

    private static readonly string[] StockHeaderPrefixes = ["saldo", "stock", "existencia", "inventario"];

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

        var aggregators = new Dictionary<string, ProductAggregate>(StringComparer.OrdinalIgnoreCase);

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

            var year = ParseYear(worksheet, file);
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var headers = Enumerable.Range(1, lastColumn)
                .Select(column => worksheet.Cell(1, column).GetString())
                .ToList();
            if (headers.Any(header => header.Trim().EndsWith("_StockInicial", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"'{file}' uses the stock-aware format (monthly sales + StockInicial columns). Run PedidoSugerido.CliStockAware instead.");
            }

            if (lastColumn < 17)
            {
                throw new InvalidOperationException(
                    $"Worksheet '{worksheet.Name}' in '{file}' must have at least 17 columns.");
            }

            // Months are read by position, so an unrecognized stock header must stop the run:
            // guessing "no stock column" would zero every stock and shift every month by one.
            var stockHeader = worksheet.Cell(1, StockColumn).GetString().Trim();
            if (!IsStockHeader(stockHeader))
            {
                throw new InvalidOperationException(
                    $"Column {StockColumn} of worksheet '{worksheet.Name}' in '{file}' is '{stockHeader}'; " +
                    "expected the current stock column (a header starting with Saldo, Stock, Existencia or Inventario, e.g. 'SaldoActual').");
            }

            for (var row = 2; row <= lastRow; row++)
            {
                var code = worksheet.Cell(row, 1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(code) || code.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = worksheet.Cell(row, 2).GetString().Trim();
                var model = worksheet.Cell(row, 3).GetString().Trim();
                var stock = ParseNumericCell(worksheet.Cell(row, StockColumn)) ?? 0m;

                if (!aggregators.TryGetValue(code, out var aggregate))
                {
                    aggregate = new ProductAggregate(code);
                    aggregators.Add(code, aggregate);
                }

                aggregate.RegisterRow(year, $"'{Path.GetFileName(file)}' row {row}");
                aggregate.UpdateIdentity(name, model);
                aggregate.UpdateStock(year, stock, name, model);

                for (var monthIndex = 0; monthIndex < 12; monthIndex++)
                {
                    var value = ParseNumericCell(worksheet.Cell(row, FirstMonthColumn + monthIndex));
                    if (value is null)
                    {
                        continue;
                    }

                    aggregate.SetSale(year, monthIndex, decimal.Truncate(value.Value));
                }
            }
        }

        var products = aggregators
            .Values
            .Select(x => x.ToProductHistory())
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (readFiles.Count == 0)
        {
            throw new InvalidOperationException($"No sales .xlsx files were found in '{inputDirectory}'.");
        }

        return new InventoryDataset(products, readFiles);
    }

    // Previous pedido outputs often sit next to the sales files; they always start with this sheet.
    private static bool IsPedidoOutput(XLWorkbook workbook) => ClosedXmlReportWriter.IsReport(workbook);

    private static IXLWorksheet? SelectDataWorksheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(sheet =>
            !sheet.Name.Equals("Control", StringComparison.OrdinalIgnoreCase) &&
            sheet.Name.IndexOf("Exclu", StringComparison.OrdinalIgnoreCase) < 0);

    private static int ParseYear(IXLWorksheet worksheet, string filePath)
    {
        var header = worksheet.Cell(1, 17).GetString().Trim();
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

    // Ignores case, whitespace and accents: "Saldo Actual", "SALDOACTUAL", "Sáldo", "Existencias" all match.
    private static bool IsStockHeader(string header)
    {
        var normalized = new StringBuilder(header.Length);
        foreach (var ch in header.Normalize(NormalizationForm.FormD))
        {
            if (char.IsWhiteSpace(ch) ||
                CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            normalized.Append(char.ToLowerInvariant(ch));
        }

        var value = normalized.ToString();
        return StockHeaderPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
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

    private static decimal? ParseNumericCell(IXLCell cell) => NumericCellParser.Parse(cell);

    private sealed class ProductAggregate
    {
        private readonly Dictionary<(int Year, int MonthIndex), decimal> _sales = new();
        private readonly Dictionary<int, string> _rowByYear = new();

        // A second row for the same product and year (duplicate row, or two files for one year)
        // would silently replace the first one's months, so it stops the run instead.
        public void RegisterRow(int year, string location)
        {
            if (_rowByYear.TryGetValue(year, out var previous))
            {
                throw new InvalidOperationException(
                    $"Product '{Code}' appears more than once for {year}: {previous} and {location}. " +
                    "Each product must appear once per year; remove the duplicate row or the extra file for that year.");
            }

            _rowByYear[year] = location;
        }

        public ProductAggregate(string code)
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

        public void SetSale(int year, int monthIndex, decimal value) => _sales[(year, monthIndex)] = value;

        public ProductHistory ToProductHistory()
        {
            var sales = _sales
                .OrderBy(x => x.Key.Year)
                .ThenBy(x => x.Key.MonthIndex)
                .Select(x => new MonthlySale(x.Key.Year, x.Key.MonthIndex, x.Value))
                .ToList();

            return new ProductHistory(Code, Name, Model, CurrentStock, StockYear, sales);
        }
    }
}
