using PedidoSugerido.Core.Options;

namespace PedidoSugerido.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void FromArgs_ParsesExplicitCoverageDates()
    {
        var args = new[]
        {
            "--input-dir", "C:\\datos",
            "--output", "C:\\salida\\pedido.xlsx",
            "--coverage-start-date", "2026-04-01",
            "--coverage-end-date", "2027-04-30",
            "--delivery-date", "2026-07-15",
            "--today", "2026-03-20"
        };

        var options = PedidoSugeridoCliOptions.FromArgs(args);

        Assert.True(options.IsValid);
        Assert.Equal(new DateOnly(2026, 4, 1), options.Coverage.StartDate);
        Assert.Equal(new DateOnly(2027, 4, 30), options.Coverage.EndDate);
    }

    [Fact]
    public void FromArgs_StillSupportsLegacyMonthIndexes()
    {
        var args = new[]
        {
            "--input-dir", "C:\\datos",
            "--output", "C:\\salida\\pedido.xlsx",
            "--coverage-start", "5",
            "--coverage-end", "9",
            "--delivery-date", "2026-07-15",
            "--today", "2026-03-20"
        };

        var options = PedidoSugeridoCliOptions.FromArgs(args);

        Assert.True(options.IsValid);
        Assert.Equal(new DateOnly(2026, 6, 1), options.Coverage.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 31), options.Coverage.EndDate);
    }
}
