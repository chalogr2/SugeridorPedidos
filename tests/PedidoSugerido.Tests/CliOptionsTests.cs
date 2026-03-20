using PedidoSugerido.Core.Options;

namespace PedidoSugerido.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void FromArgs_ParsesExpectedValues()
    {
        var args = new[]
        {
            "--input-dir", "C:\\datos",
            "--output", "C:\\salida\\pedido.xlsx",
            "--coverage-start", "6",
            "--coverage-end", "11",
            "--delivery-date", "2026-07-15",
            "--today", "2026-03-20"
        };

        var options = PedidoSugeridoCliOptions.FromArgs(args);

        Assert.True(options.IsValid);
        Assert.Equal("C:\\datos", options.InputDirectory);
        Assert.Equal("C:\\salida\\pedido.xlsx", options.OutputPath);
        Assert.Equal(6, options.CoverageStartMonthIndex);
        Assert.Equal(11, options.CoverageEndMonthIndex);
        Assert.Equal(new DateOnly(2026, 7, 15), options.DeliveryDate);
        Assert.Equal(new DateOnly(2026, 3, 20), options.Today);
    }
}
