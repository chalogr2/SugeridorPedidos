# Purchase Order Suggester

A command-line tool that reads a few years of monthly sales history from Excel and tells you how much of each product to order for an upcoming period. It runs several forecasting methods side by side, so you can compare them before deciding, and writes the results to a new Excel workbook.

I built it for my own business and use it for real purchasing. Everything runs locally: no database, no cloud services, no web app. The Excel files are read and written with [ClosedXML](https://github.com/ClosedXML/ClosedXML).

## How it works

1. You export one Excel file per year with sales per product per month, plus current stock.
2. You tell the tool which months the order needs to cover and when it is expected to arrive.
3. It combines all years by product code and works out, for each product, how many units will be needed between today and the end of the coverage period.
4. It subtracts current stock and writes a suggested order for each method.

There are two programs, one for each kind of export:

| Program | Input |
| --- | --- |
| `PedidoSugerido.Cli` | Monthly units sold, one column per month |
| `PedidoSugerido.CliStockAware` | Monthly units sold plus the stock at the start of each month, so months where the product was out of stock can be corrected |

## Requirements

- .NET 8 SDK
- Windows with PowerShell (the examples use PowerShell line continuations)

```powershell
dotnet --version
```

## Build and test

From the repository root:

```powershell
dotnet build .\PedidoSugerido.sln
dotnet test .\PedidoSugerido.sln
```

## Quick start

```powershell
dotnet run --project .\src\PedidoSugerido.Cli -- `
  --input-dir "C:\Path\To\SalesFiles" `
  --output "C:\Path\To\Output\suggested-order.xlsx" `
  --coverage-start-date 2027-01-01 `
  --coverage-end-date 2027-12-31 `
  --delivery-date 2026-12-31 `
  --today 2026-11-17
```

This asks: given today's date, how much should I order so it arrives by December 31 and covers all of 2027? Run either program with `--help` to see every option.

## Command-line options

| Option | Required | Description |
| --- | --- | --- |
| `--input-dir` | Yes | Folder with the yearly sales files (`.xlsx`) |
| `--output` | Yes | Report to create (`.xlsx`) |
| `--coverage-start-date` | Yes* | First month the order must cover (`yyyy-MM-dd`, the day is ignored) |
| `--coverage-end-date` | Yes* | Last month the order must cover (`yyyy-MM-dd`, the day is ignored) |
| `--delivery-date` | Yes | Date the order is expected to arrive (`yyyy-MM-dd`) |
| `--today` | Yes | Reference date for the calculation (`yyyy-MM-dd`) |
| `--tres-columnas` | No | Stock-aware program only. Use it when the files have 3 columns per month instead of 2 (see below) |
| `--help` | No | Show usage |

\* Instead of coverage dates you can pass the older `--coverage-start` and `--coverage-end` month indexes, where 0 is January and 11 is December. They are relative to `--today`, so `--coverage-start 5 --coverage-end 9` with `--today 2026-03-20` covers June to October 2026. If the end month comes before the start month, the coverage runs into the next year.

The tool is strict about its arguments, because a quietly wrong date means a wrong order:

- An unknown option, an option without a value, a repeated option or an invalid value stops the run with exit code 1 and a message explaining what is wrong.
- Dates must be in `yyyy-MM-dd` format.
- The delivery date cannot be before today, and the coverage cannot end before it starts.
- If values are missing and you are in a terminal, the tool asks for them. If it is running from a script or scheduled task, missing values are an error. It never fills in defaults on its own.

## Input files

### Basic format (`PedidoSugerido.Cli`)

One `.xlsx` file per year. The tool uses the first worksheet whose name is not `Control` and does not contain `Exclu`, so you can keep helper sheets in the same workbook.

| Column | Content |
| --- | --- |
| 1 | Product code |
| 2 | Product name |
| 3 | Product model |
| 4 | Current stock |
| 5 to 16 | Units sold, January to December |
| 17 | Yearly total, with the year in the header (for example `TotalAnual2025`) |

The header of column 4 must start with `Saldo`, `Stock`, `Existencia` or `Inventario` (case, spaces and accents are ignored). If it doesn't, the run stops. Months are read by position, so guessing wrong here would set every product's stock to zero and shift every month by one.

### Stock-aware format (`PedidoSugerido.CliStockAware`)

Columns 1 to 4 are product code, model, name and current stock. After that, each month from January to December has either:

- **2 columns:** units sold, stock at the start of the month
- **3 columns:** units sold, sales amount before tax, stock at the start of the month (add `--tres-columnas`)

The stock column header for each month must contain the word "stock", and the sales header must not. If the file doesn't match the layout you chose, the tool stops and tells you whether to add or remove `--tres-columnas`.

When a month started with zero stock and sold nothing, the product simply wasn't available, so that zero isn't real demand. The tool replaces it with the median of the same month in years where the product was in stock. If there are none, it uses the median of all in-stock months.

### Rules for both formats

- **Year:** taken from the header of the yearly total column. If that doesn't work, it's taken from the file name (for example `sales_2025.xlsx`). If neither has a year, the run stops.
- **Several years:** all files are merged by product code. Current stock comes from the most recent year.
- **Duplicates:** a product can appear only once per year. A repeated code in a sheet, or two files for the same year, stops the run with an error that names both locations.
- **Empty cells:** empty cells and cells that say `no data` are treated as missing, not as zero.
- **Numbers stored as text:** these are accepted when unambiguous, such as `1234`, `12.5`, `12,5`, `1,234` or `1.234,5`. Any other text in a number column stops the run.
- **Skipped files:** files with `pedido` in the name, and previous reports from this tool, are skipped. You can keep old reports in the same folder.

## Forecasting methods

Each method estimates demand for the order period and subtracts current stock. Results are rounded up, and anything below zero becomes zero.

The **bridge** is the months from the current month up to the coverage start. The order has to last through them too, because current stock is used up during that time.

**González Ruiz.** For each past year, add up that year's sales for the bridge and coverage months. A month is counted as many times as it appears in the period, so a 14-month period with two Novembers counts November twice. Subtract stock from each year's total, then average across years. A year is only used if it has data for every month in the period, so a half-finished current year doesn't drag the average down. If a product has no complete year, partial years are used and the tool prints a warning.

**González Ruiz per-month.** The same period, but each month's demand is the average of that calendar month across every year that has it. This includes the real months of an unfinished year. Stock is subtracted once at the end. When all years are complete, it usually matches González Ruiz. It differs when stock sits between what different years needed: González Ruiz subtracts stock year by year before averaging, while this method averages first.

**Average.** Takes the average of every month in the history and multiplies it by the coverage months plus the lead time. The lead time is the time between today and delivery.

**Minimum.** The same as Average, but using the lowest month in the history.

**Maximum.** The same as Average, but using the highest month in the history.

**Exponential smoothing.** Smooths the full monthly series in date order (alpha 0.3). The smoothed value is used as the monthly demand.

**Seasonal.** For each coverage month, takes the average of that calendar month across all years. Lead time demand is added on top, using the overall monthly average.

**Weighted recommendation.** A weighted blend of González Ruiz, Seasonal, Exponential smoothing, Average, Minimum and Maximum. The weights depend on whether the product is highly seasonal and on its ABC class, which ranks products by total historical demand. The per-month variant is not included in the blend.

## Output

A workbook with four sheets:

1. All products
2. Products with any suggested order above zero
3. The same as sheet 2, without the Maximum column
4. The same as sheet 3, without the weighted recommendation

Each sheet has the product code, name, model, current stock and one column per method, with frozen headers and filters. The two González Ruiz columns and the weighted recommendation are highlighted.

If the output file already exists, it is only replaced when it is a previous report from this tool. A sales file or any other workbook is never overwritten.

## Project structure

```text
src/PedidoSugerido.Core            Readers, forecasting engine, report writer, CLI options
src/PedidoSugerido.Cli             Program for the basic format
src/PedidoSugerido.CliStockAware   Program for the stock-aware format
tests/PedidoSugerido.Tests         xUnit tests
```

## Troubleshooting

If PowerShell complains about options like `--input-dir`, make sure you pasted the whole command. Each line has to end with a backtick (`` ` ``) so PowerShell treats them as one command.

If the report can't be written, check that it isn't open in Excel.

## License

Copyright (c) 2026 Fernando González Ruiz. All rights reserved. See [LICENSE](LICENSE).
