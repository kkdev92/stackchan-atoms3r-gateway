using System.Text;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Conformance.Tests;

/// <summary>
/// Verifies that each conformance rule detects its corresponding response mutation.
/// </summary>
/// <remarks>
/// Mutates one element of a valid response at a time and checks the reported violation number.
/// </remarks>
[Collection(ConformanceCollection.Name)]
public sealed class WireMutationTests(ConformanceFixture fixture)
{
    [Fact]
    public void 変更前の応答には_違反が無い()
    {
        Run(fixture.Body, fixture.ContentType).ShouldBeEmpty();
    }

    [Fact]
    public void 不正な_Content_Type_を検出する()
    {
        Detects(1, fixture.Body, "text/plain; charset=utf-8");
    }

    [Fact]
    public void イベント末尾の空行不足を検出する()
    {
        // Remove one of the two newlines that delimit the final event.
        var text = Text();
        var broken = text[..^1];

        Detects(2, Bytes(broken));
    }

    [Fact]
    public void 複数行にまたがるSSEイベントを検出する()
    {
        var text = Text().Replace(
            "\"name\":\"conversation.text\"",
            "\"name\":\n\"conversation.text\"",
            StringComparison.Ordinal);

        Detects(2, Bytes(text));
    }

    [Fact]
    public void 未対応のエンベロープ版を検出する()
    {
        Detects(3, Bytes(Text().Replace("\"v\":1", "\"v\":2", StringComparison.Ordinal)));
    }

    [Fact]
    public void 不正なエンベロープ種別を検出する()
    {
        Detects(
            3,
            Bytes(Text().Replace("\"kind\":\"event\"", "\"kind\":\"result\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void 日本語の_Unicodeエスケープを検出する()
    {
        var text = Text().Replace(
            ConformanceFixture.Transcript,
            string.Concat(ConformanceFixture.Transcript.Select(c => $"\\u{(int)c:X4}")),
            StringComparison.Ordinal);

        Detects(4, Bytes(text));
    }

    [Fact]
    public void Base64のプラス記号の_Unicodeエスケープを検出する()
    {
        var text = Text().Replace("+", "\\u002B", StringComparison.Ordinal);

        Detects(4, Bytes(text));
    }

    [Fact]
    public void 上限を超えるSSEイベントを検出する()
    {
        var text = Text();
        var target = "\"text\":\"" + ConformanceFixture.FirstSentence + "\"";
        var padded = "\"text\":\"" + ConformanceFixture.FirstSentence + new string('あ', 4000) + "\"";

        Detects(5, Bytes(text.Replace(target, padded, StringComparison.Ordinal)));
    }

    [Fact]
    public void 音声のシーケンス番号の欠番を検出する()
    {
        Detects(6, Bytes(Text().Replace("\"seq\":1,", "\"seq\":3,", StringComparison.Ordinal)));
    }

    [Fact]
    public void 音声のシーケンス番号が_1_から始まる違反を検出する()
    {
        Detects(6, Bytes(Text().Replace("\"seq\":0,", "\"seq\":1,", StringComparison.Ordinal)));
    }

    [Fact]
    public void 音声レートが_16000_Hz以外なら違反を検出する()
    {
        Detects(7, Bytes(Text().Replace("\"rate\":16000", "\"rate\":44100", StringComparison.Ordinal)));
    }

    [Fact]
    public void 上限を超えるPCMを検出する()
    {
        var text = Text();
        var events = SseWire.Events(fixture.Body);
        var pcm = events
            .First(wire => wire.Name == "reply.audio")
            .Payload.GetProperty("pcm").GetString() ?? "";

        // Replace the value with Base64 that decodes to more than 4,096 bytes.
        var oversized = Convert.ToBase64String(new byte[4098]);

        Detects(8, Bytes(text.Replace(pcm, oversized, StringComparison.Ordinal)));
    }

    [Fact]
    public void 奇数バイトのPCMを検出する()
    {
        var text = Text();
        var pcm = SseWire.Events(fixture.Body)
            .First(wire => wire.Name == "reply.audio")
            .Payload.GetProperty("pcm").GetString() ?? "";

        var odd = Convert.ToBase64String(new byte[401]);

        Detects(8, Bytes(text.Replace(pcm, odd, StringComparison.Ordinal)));
    }

    [Fact]
    public void dataプレフィックスを含めて_上限を超える行を検出する()
    {
        // The firmware's 8,192-byte line buffer includes the "data: " prefix.
        var text = Text();
        var target = "\"text\":\"" + ConformanceFixture.FirstSentence + "\"";
        var envelope = SseWire.Events(fixture.Body)
            .First(wire => wire.Json.Contains(
                ConformanceFixture.FirstSentence, StringComparison.Ordinal));

        // Use a three-byte Japanese character so only the complete prefixed line exceeds the limit.
        var room = 8193 - ConformanceChecks.DataFieldPrefixBytes - envelope.JsonByteLength;
        var padded = "\"text\":\"" + ConformanceFixture.FirstSentence +
            new string('あ', (room + 2) / 3) + "\"";

        var body = Bytes(text.Replace(target, padded, StringComparison.Ordinal));

        // It is a violation when the full SSE line exceeds the limit even if JSON alone does not.
        SseWire.Events(body)
            .Max(wire => wire.JsonByteLength)
            .ShouldBeLessThanOrEqualTo(ConformanceChecks.MaxEventBytes);
        Detects(5, body);
    }

    [Fact]
    public void Base64のPCMに含まれる空白を検出する()
    {
        // The .NET Base64 decoder ignores whitespace, but the firmware rejects it.
        var pcm = FirstPcm();

        Detects(8, Bytes(Text().Replace(
            "\"pcm\":\"" + pcm + "\"",
            "\"pcm\":\"" + pcm[..8] + " " + pcm[8..] + "\"",
            StringComparison.Ordinal)));
    }

    [Fact]
    public void JSONエスケープを含むPCMを検出する()
    {
        // The firmware decodes PCM without resolving JSON escapes.
        var pcm = FirstPcm();
        var escaped = pcm.Replace("A", @"\/", StringComparison.Ordinal);

        Detects(8, Bytes(Text().Replace(
            "\"pcm\":\"" + pcm + "\"",
            "\"pcm\":\"" + escaped + "\"",
            StringComparison.Ordinal)));
    }

    [Fact]
    public void 上限を超えるテキストを検出する()
    {
        var text = Text();
        var target = "\"text\":\"" + ConformanceFixture.SecondSentence + "\"";

        // The Japanese character used here is three UTF-8 bytes, so 200 characters total 600 bytes.
        var oversized = "\"text\":\"" + new string('い', 200) + "\"";

        Detects(9, Bytes(text.Replace(target, oversized, StringComparison.Ordinal)));
    }

    [Fact]
    public void 分割された同じ文で_テキストを再送する違反を検出する()
    {
        // The first sentence has 2,500 samples and spans two audio events. Add the same text to the
        // second event, which normally has none, to create duplicate firmware captions.
        var text = Text();
        var target = "{\"seq\":1,\"rate\":16000,";
        var broken = "{\"seq\":1,\"text\":\"" + ConformanceFixture.FirstSentence +
            "\",\"rate\":16000,";

        text.ShouldContain(target);

        Detects(10, Bytes(text.Replace(target, broken, StringComparison.Ordinal)));
    }

    [Fact]
    public void 別々の文が同じテキストでも_違反にしない()
    {
        // Repeated text itself is allowed as long as it appears only in each sentence's first event.
        var text = Text();
        var first = "\"text\":\"" + ConformanceFixture.FirstSentence + "\",";
        var second = "\"text\":\"" + ConformanceFixture.SecondSentence + "\",";

        var repeated = text.Replace(second, first, StringComparison.Ordinal);

        Run(Bytes(repeated), fixture.ContentType).ShouldBeEmpty();
    }

    [Fact]
    public void lastが無い音声ストリームを検出する()
    {
        Detects(11, Bytes(Text().Replace("\"last\":true", "\"last\":false", StringComparison.Ordinal)));
    }

    [Fact]
    public void lastが複数ある音声ストリームを検出する()
    {
        Detects(11, Bytes(Text().Replace("\"last\":false", "\"last\":true", StringComparison.Ordinal)));
    }

    [Fact]
    public void conversation_finishedが無い応答を検出する()
    {
        var text = Text();
        var start = text.IndexOf("data: {\"v\":1,\"kind\":\"event\",\"name\":\"conversation.finished\"", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0);

        Detects(12, Bytes(text[..start]));
    }

    [Fact]
    public void conversation_finishedより後のイベントを検出する()
    {
        var text = Text();
        var extra = "data: {\"v\":1,\"kind\":\"event\",\"name\":\"reply.audio\"," +
            "\"payload\":{\"seq\":99,\"rate\":16000,\"pcm\":\"\",\"last\":false}}\n\n";

        Detects(12, Bytes(text + extra));
    }

    [Fact]
    public void 未対応の表情マーカーを検出する()
    {
        var text = Text().Replace("[happy]", "[excited]", StringComparison.Ordinal);

        Detects(13, Bytes(text));
    }

    private string Text() => Encoding.UTF8.GetString(fixture.Body);

    /// <summary>Gets Base64 PCM from the first non-empty audio event.</summary>
    private string FirstPcm() =>
        SseWire.Events(fixture.Body)
            .First(wire => wire.Name == "reply.audio" &&
                (wire.Payload.GetProperty("pcm").GetString() ?? "").Length > 0)
            .Payload.GetProperty("pcm").GetString()!;

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static IReadOnlyList<ConformanceViolation> Run(byte[] body, string? contentType) =>
        ConformanceChecks.Run(
            contentType,
            body,
            [ConformanceFixture.Transcript, "こんにちは"]);

    private void Detects(int number, byte[] body, string? contentType = null)
    {
        var violations = Run(body, contentType ?? fixture.ContentType);

        violations.ShouldContain(
            violation => violation.Number == number,
            $"プロトコル規則 {number} の違反が報告されていない。報告内容: " +
            string.Join(" / ", violations.Select(v => v.ToString())));
    }
}
