using System.Diagnostics;
using System.Text;
using Kkdev92.StackChan.Gateway.AgentFramework.Models;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Tests;

/// <summary>Verifies parser limits for malformed or oversized model responses.</summary>
/// <remarks>
/// Model responses are untrusted input. Limits prevent buffer growth from unclosed tags, excess work
/// and invocation count from many tool calls, and history growth from huge arguments. Unparseable input
/// is preserved as regular response text.
/// </remarks>
public sealed class ParserLimitsTests
{
    private const string Call =
        """<tool_call>{"name":"get_current_time","arguments":{}}</tool_call>""";

    [Theory]
    [InlineData(50_000)]
    [InlineData(400_000)]
    public void 閉じないタグを流し続けても_保留サイズは上限で止まる(int total)
    {
        // Feed an unclosed block across multiple chunks.
        var buffer = new StringBuilder("<tool_call>");
        var chunk = new string('x', 64);
        var maxPending = 0;
        var watch = Stopwatch.StartNew();

        for (var sent = 0; sent < total; sent += chunk.Length)
        {
            buffer.Append(chunk);
            ToolCallText.DrainBuffer(buffer, flush: false);
            maxPending = Math.Max(maxPending, buffer.Length);
        }

        // Because limits are checked by chunk, allow at most one chunk of overshoot.
        maxPending.ShouldBeLessThan(
            ToolCallText.MaxPendingChars + chunk.Length + 32,
            $"保留が {maxPending} 文字まで伸びた");

        // Also verify processing has not regressed to quadratic complexity in input size.
        watch.ElapsedMilliseconds.ShouldBeLessThan(2000);
    }

    [Fact]
    public void 通常の本文は_保留せずに返す()
    {
        // Ensure limit handling does not affect ordinary streaming responses.
        var buffer = new StringBuilder();
        var seen = new StringBuilder();

        foreach (var piece in new[] { "こんにちは", "、スタックちゃん", "です。" })
        {
            buffer.Append(piece);
            var (calls, text) = ToolCallText.DrainBuffer(buffer, flush: false);
            calls.ShouldBeEmpty();
            seen.Append(text);
        }

        seen.ToString().ShouldBe("こんにちは、スタックちゃんです。");
        buffer.Length.ShouldBe(0);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(20_000)]
    public void 大量のツール呼び出しでも_上限件数で解析を打ち切る(int count)
    {
        var body = new StringBuilder();
        for (var index = 0; index < count; index++)
        {
            body.Append(Call);
        }

        var buffer = new StringBuilder(body.ToString());
        var watch = Stopwatch.StartNew();

        var (calls, _) = ToolCallText.DrainBuffer(buffer, flush: true);

        calls.Count.ShouldBe(ToolCallText.MaxCallsPerResponse);

        // Stop scanning input as well as limiting result count to prevent excessive processing time.
        watch.ElapsedMilliseconds.ShouldBeLessThan(
            500,
            $"処理に {watch.ElapsedMilliseconds} ms かかり、500 ms の上限を超えました。");
    }

    [Fact]
    public void 上限件数までのツール呼び出しは_すべて解析する()
    {
        var body = new StringBuilder();
        for (var index = 0; index < ToolCallText.MaxCallsPerResponse; index++)
        {
            // Vary arguments so calls are not deduplicated.
            body.Append("<tool_call>{\"name\":\"get_current_time\",\"arguments\":{\"n\":")
                .Append(index)
                .Append("}}</tool_call>");
        }

        var buffer = new StringBuilder(body.ToString());
        var (calls, _) = ToolCallText.DrainBuffer(buffer, flush: true);

        calls.Count.ShouldBe(ToolCallText.MaxCallsPerResponse);
    }

    [Theory]
    [InlineData(1_000, 1)]
    [InlineData(100_000, 0)]
    [InlineData(2_000_000, 0)]
    public void 上限を超える引数は_ツール呼び出しとして解析しない(int size, int expected)
    {
        var buffer = new StringBuilder();
        buffer.Append("<tool_call>{\"name\":\"get_current_time\",\"arguments\":{\"q\":\"");
        buffer.Append(new string('a', size));
        buffer.Append("\"}}</tool_call>");

        var (calls, text) = ToolCallText.DrainBuffer(buffer, flush: true);

        calls.Count.ShouldBe(expected);

        if (expected == 0)
        {
            // Preserve input over the limit as regular response text.
            text.Length.ShouldBeGreaterThan(size);
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(64)]
    [InlineData(5_000)]
    public void 深くネストした_JSON_でも例外を投げない(int depth)
    {
        // Treat JSON over JsonDocument's depth limit as response text, not a tool call.
        var buffer = new StringBuilder();
        buffer.Append("""<tool_call>{"name":"get_current_time","arguments":""");
        buffer.Append(new string('[', depth));
        buffer.Append(new string(']', depth));
        buffer.Append("}</tool_call>");

        var (calls, text) = ToolCallText.DrainBuffer(buffer, flush: true);

        // The parser must not propagate exceptions regardless of parse success.
        (calls.Count + text.Length).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ランダムな入力を_2_万件処理しても_例外を投げない()
    {
        // Fix the random seed so a failing input can be reproduced.
        var random = new Random(20260827);
        var alphabet = "<>/{}[]\"':,tool_call name arguments abc0123\\n\u0000\uD83D\uDE00".ToCharArray();

        for (var round = 0; round < 20_000; round++)
        {
            var length = random.Next(0, 120);
            var chars = new char[length];

            for (var index = 0; index < length; index++)
            {
                chars[index] = alphabet[random.Next(alphabet.Length)];
            }

            var input = new string(chars);
            var buffer = new StringBuilder(input);
            var flush = random.Next(2) == 0;

            try
            {
                ToolCallText.DrainBuffer(buffer, flush);
            }
            catch (Exception exception)
            {
                Assert.Fail(
                    $"round={round} flush={flush} {exception.GetType().Name}: {exception.Message}\n" +
                    $"入力={input.Replace("\n", "\n", StringComparison.Ordinal)}");
            }
        }
    }
}
