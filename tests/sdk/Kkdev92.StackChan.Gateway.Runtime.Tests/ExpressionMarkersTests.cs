using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Runtime.Text;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>
/// Verifies the mapping between expressions and text markers.
/// </summary>
/// <remarks>
/// Independently of enum ordering, every <see cref="SpeechExpression"/> must map to exactly
/// one marker, with no duplicates.
/// </remarks>
public sealed class ExpressionMarkersTests
{
    [Fact]
    public void すべての表情が_マーカーと_1_対_1_で対応する()
    {
        var expressions = Enum.GetValues<SpeechExpression>();

        ExpressionMarkers.All.Length.ShouldBe(expressions.Length);

        var read = new List<SpeechExpression>();

        foreach (var marker in ExpressionMarkers.All)
        {
            ExpressionMarkers.TryRead(marker + "そうですね。", out var expression, out var spoken)
                .ShouldBeTrue($"'{marker}' を表情マーカーとして解析できません。");
            spoken.ShouldBe("そうですね。");

            read.Add(expression);
        }

        // Detects both missing and duplicated enum values.
        read.Distinct().Count().ShouldBe(expressions.Length);
        read.ShouldBe(expressions, ignoreOrder: true);
    }

    [Fact]
    public void 未知の括弧書きは_表情マーカーとして扱わない()
    {
        ExpressionMarkers.TryRead("[foo]そうですね。", out var expression, out var spoken)
            .ShouldBeFalse();

        expression.ShouldBe(SpeechExpression.Neutral);
        spoken.ShouldBe("[foo]そうですね。");
    }
}
