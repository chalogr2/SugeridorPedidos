using PedidoSugerido.Core.Services;

namespace PedidoSugerido.Tests;

public sealed class NumericCellParserTests
{
    [Theory]
    [InlineData("12", "12")]
    [InlineData("-3", "-3")]
    [InlineData("12.5", "12.5")]
    [InlineData("12,5", "12.5")]
    [InlineData("1,23", "1.23")]
    [InlineData("1,234", "1234")]
    [InlineData("1,234,567", "1234567")]
    [InlineData("1,234.5", "1234.5")]
    [InlineData("1.234,5", "1234.5")]
    [InlineData("1.234.567", "1234567")]
    [InlineData("1.234", "1.234")]
    [InlineData(" 42 ", "42")]
    public void TryParseText_ReadsUnambiguousShapes(string text, string expected)
    {
        Assert.True(NumericCellParser.TryParseText(text, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12,5,1")]
    [InlineData("12.5.1")]
    [InlineData("$12")]
    [InlineData("2026-01-31")]
    public void TryParseText_RejectsEverythingElse(string text)
    {
        Assert.False(NumericCellParser.TryParseText(text, out _));
    }
}
