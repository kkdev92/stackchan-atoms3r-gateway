using System.Globalization;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Capabilities.Tests;

/// <summary>Verifies the rules that format numbers into text for speech.</summary>
public sealed class SpokenTextTests
{
    [Theory]
    [InlineData(28.34, "28.3")]
    [InlineData(28.0, "28")]
    [InlineData(0.04, "0")]
    [InlineData(1234.5, "1234.5")]  // No digit grouping (it breaks the spoken form)
    // As in the .NET default, an exact midpoint rounds to the nearest even value.
    [InlineData(-3.55, "-3.6")]
    [InlineData(-3.45, "-3.4")]
    public void 小数第_1_位へ丸める(double value, string expected)
    {
        SpokenText.Number(value).ShouldBe(expected);
    }

    [Fact]
    public void 小数部の桁数を指定できる()
    {
        SpokenText.Number(3.14159, 3).ShouldBe("3.142");
        SpokenText.Number(3.14159, 0).ShouldBe("3");
    }

    [Fact]
    public void カルチャに依存せず小数点を使用する()
    {
        // Even where the culture uses a comma as the decimal separator, the spoken value is "28.3".
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            SpokenText.Number(28.34).ShouldBe("28.3");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 負の桁数を拒否する()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SpokenText.Number(1.0, -1));
    }
}
