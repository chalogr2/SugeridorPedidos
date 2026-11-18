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

    private static readonly string[] ValidArgs =
    [
        "--input-dir", "C:\\datos",
        "--output", "C:\\salida\\pedido.xlsx",
        "--coverage-start-date", "2027-01-01",
        "--coverage-end-date", "2027-12-31",
        "--delivery-date", "2026-12-31",
        "--today", "2026-11-17"
    ];

    private static string[] With(string flag, string value)
    {
        var args = ValidArgs.ToArray();
        args[Array.IndexOf(args, flag) + 1] = value;
        return args;
    }

    [Fact]
    public void Resolve_AcceptsValidArguments()
    {
        var options = PedidoSugeridoCliOptions.Resolve(ValidArgs, inputRedirected: true);

        Assert.Equal(new DateOnly(2026, 11, 17), options.Today);
        Assert.Equal(new DateOnly(2026, 12, 31), options.DeliveryDate);
    }

    [Theory]
    [InlineData("--today", "2026-13-45", "Invalid date for --today")]
    [InlineData("--today", "17/11/2026", "Invalid date for --today")]
    [InlineData("--delivery-date", "2026-11-01", "is before --today")]
    [InlineData("--coverage-end-date", "2026-12-31", "is before coverage start")]
    [InlineData("--output", "C:\\salida\\pedido.csv", "must be an .xlsx file")]
    public void Resolve_RejectsInvalidValues_InsteadOfSubstitutingDefaults(string flag, string value, string expectedMessage)
    {
        var error = Assert.Throws<ArgumentException>(
            () => PedidoSugeridoCliOptions.Resolve(With(flag, value), inputRedirected: false));

        Assert.Contains(expectedMessage, error.Message);
    }

    [Fact]
    public void Resolve_RejectsUnknownArguments()
    {
        var error = Assert.Throws<ArgumentException>(
            () => PedidoSugeridoCliOptions.Resolve([.. ValidArgs, "--todya", "2026-11-17"], inputRedirected: false));

        Assert.Contains("Unknown argument '--todya'", error.Message);
    }

    [Fact]
    public void Resolve_RejectsFlagWithoutValue()
    {
        var args = ValidArgs.Where((_, i) => i != 1).ToArray(); // drop the --input-dir value

        var error = Assert.Throws<ArgumentException>(
            () => PedidoSugeridoCliOptions.Resolve(args, inputRedirected: false));

        Assert.Contains("Missing value for --input-dir", error.Message);
    }

    [Fact]
    public void Resolve_MissingValuesWithoutTerminal_FailsInsteadOfPrompting()
    {
        var args = ValidArgs.Take(ValidArgs.Length - 2).ToArray(); // drop --today

        var error = Assert.Throws<ArgumentException>(
            () => PedidoSugeridoCliOptions.Resolve(args, inputRedirected: true));

        Assert.Contains("Missing --today", error.Message);
    }

    [Fact]
    public void Resolve_RejectsMixedCoverageStyles()
    {
        var error = Assert.Throws<ArgumentException>(
            () => PedidoSugeridoCliOptions.Resolve([.. ValidArgs, "--coverage-start", "5"], inputRedirected: false));

        Assert.Contains("not both", error.Message);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void IsHelpRequested_DetectsHelpFlags(string flag)
    {
        Assert.True(PedidoSugeridoCliOptions.IsHelpRequested(["--today", "2026-11-17", flag]));
    }
}
