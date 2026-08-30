using System.Text;
using System.Text.Json;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Conformance.Tests;

/// <summary>
/// Checks the SSE response the gateway produces against the Atoms3R protocol rules.
/// </summary>
/// <remarks>
/// The firmware discards malformed events, so these tests run against the serialized UTF-8
/// bytes rather than against objects.
/// </remarks>
[Collection(ConformanceCollection.Name)]
public sealed class WireConformanceTests(ConformanceFixture fixture)
{
    [Fact]
    public void 実際の応答は_すべてのプロトコル規則を満たす()
    {
        var violations = ConformanceChecks.Run(
            fixture.ContentType,
            fixture.Body,
            [ConformanceFixture.Transcript, "こんにちは"]);

        violations.ShouldBeEmpty(
            "違反: " + string.Join(" / ", violations.Select(v => v.ToString())));
    }

    [Fact]
    public void Content_Typeは_event_stream_である()
    {
        fixture.ContentType.ShouldBe("text/event-stream; charset=utf-8");
    }

    [Fact]
    public void SSEイベントは_data_1_行と空行で区切る()
    {
        var text = Encoding.UTF8.GetString(fixture.Body);

        text.ShouldEndWith("\n\n");
        text.ShouldNotContain("\r");

        foreach (var record in SseWire.Parse(fixture.Body))
        {
            record.Lines.Count.ShouldBe(1);
            (record.IsComment || record.Json is not null).ShouldBeTrue();
        }
    }

    [Fact]
    public void イベントエンベロープは_版_種別_名前_payload_を持つ()
    {
        var events = SseWire.Events(fixture.Body);

        events.ShouldNotBeEmpty();

        foreach (var wire in events)
        {
            using var document = JsonDocument.Parse(wire.Json);

            document.RootElement.GetProperty("v").GetInt32().ShouldBe(1);
            document.RootElement.GetProperty("kind").GetString().ShouldBe("event");
            wire.Name.ShouldNotBeNullOrEmpty();
            wire.Payload.ValueKind.ShouldBe(JsonValueKind.Object);
        }
    }

    [Fact]
    public void 日本語と_Base64_を_Unicodeエスケープしない()
    {
        var text = Encoding.UTF8.GetString(fixture.Body);

        // Send Japanese as UTF-8 so the firmware's JSON scanner can read it.
        text.ShouldContain(ConformanceFixture.Transcript);
        text.ShouldContain("こんにちは");
        text.ShouldNotContain("\\u");

        // The + inside the PCM Base64 is not Unicode-escaped either.
        var audio = SseWire.Events(fixture.Body)
            .Where(wire => wire.Name == "reply.audio")
            .Select(wire => wire.Payload.GetProperty("pcm").GetString() ?? "")
            .ToList();

        audio.ShouldContain(pcm => pcm.Contains('+'), "検証用 PCM に '+' が含まれていません。");
        text.ShouldNotContain("\\u002B");
    }

    [Fact]
    public void SSEイベントは_8192_バイト以内である()
    {
        foreach (var wire in SseWire.Events(fixture.Body))
        {
            wire.JsonByteLength.ShouldBeLessThanOrEqualTo(ConformanceChecks.MaxEventBytes);
        }
    }

    [Fact]
    public void 音声のシーケンス番号は_0_から連続する()
    {
        var sequences = Audio()
            .Select(wire => wire.Payload.GetProperty("seq").GetInt64())
            .ToList();

        sequences.ShouldNotBeEmpty();
        sequences.ShouldBe(Enumerable.Range(0, sequences.Count).Select(index => (long)index));
    }

    [Fact]
    public void 音声のサンプルレートは_常に_16000_Hzである()
    {
        Audio().ShouldAllBe(wire => wire.Payload.GetProperty("rate").GetInt32() == 16000);
    }

    [Fact]
    public void PCMは_4096_バイト以内の偶数長である()
    {
        foreach (var wire in Audio())
        {
            var encoded = wire.Payload.GetProperty("pcm").GetString() ?? "";

            if (encoded.Length == 0)
            {
                continue;
            }

            var bytes = Convert.FromBase64String(encoded);
            bytes.Length.ShouldBeLessThanOrEqualTo(ConformanceChecks.MaxPcmBytes);
            (bytes.Length % 2).ShouldBe(0);
        }
    }

    [Fact]
    public void テキストは_UTF8_で_512_バイト以内である()
    {
        foreach (var wire in SseWire.Events(fixture.Body))
        {
            if (!wire.Payload.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            Encoding.UTF8.GetByteCount(text.GetString() ?? "")
                .ShouldBeLessThanOrEqualTo(ConformanceChecks.MaxTextBytes);
        }
    }

    [Fact]
    public void 文のテキストは_最初の音声イベントだけに含める()
    {
        var audio = Audio();
        var texts = new List<string>();

        foreach (var wire in audio)
        {
            if (wire.Payload.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                texts.Add(text.GetString() ?? "");
            }
        }

        // Only the first event of each sentence carries the text; later audio events do not repeat it.
        texts.ShouldBe([ConformanceFixture.FirstSentence, ConformanceFixture.SecondSentence]);
        texts.Distinct(StringComparer.Ordinal).Count().ShouldBe(texts.Count);
        audio.Count.ShouldBeGreaterThan(texts.Count);
    }

    [Fact]
    public void lastは_最後の音声イベントだけで_true_になる()
    {
        var audio = Audio();
        var flags = audio
            .Select((wire, index) => (index, last: wire.Payload.GetProperty("last").GetBoolean()))
            .Where(pair => pair.last)
            .ToList();

        flags.ShouldHaveSingleItem().index.ShouldBe(audio.Count - 1);
    }

    [Fact]
    public void conversation_finishedは_最後のイベントとして_1回だけ送信する()
    {
        var events = SseWire.Events(fixture.Body);

        events[^1].Name.ShouldBe("conversation.finished");
        events[^1].Payload.GetProperty("reason").GetString().ShouldBe("completed");
        events.Count(wire => wire.Name == "conversation.finished").ShouldBe(1);
    }

    [Fact]
    public void 表情マーカーは_ファームウェアが対応する値に限る()
    {
        foreach (var wire in SseWire.Events(fixture.Body))
        {
            if (!wire.Payload.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = textElement.GetString() ?? "";

            if (!text.StartsWith('['))
            {
                continue;
            }

            var close = text.IndexOf(']', StringComparison.Ordinal);
            close.ShouldBeGreaterThan(1);
            SseWire.AllowedExpressionMarkers.ShouldContain(text[1..close]);
        }
    }

    private IReadOnlyList<WireEvent> Audio() =>
        [.. SseWire.Events(fixture.Body).Where(wire => wire.Name == "reply.audio")];
}
