using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>Verifies multiple protocol constraints through a conversation with the app endpoint.</summary>
/// <remarks>
/// Package unit tests cover individual constraints. This test checks their integration: recognition
/// result truncation, expression markers, audio chunking and sequence numbers, SSE line length, and completion events.
/// </remarks>
public sealed class EndToEndProtocolRegressionTests : IAsyncLifetime
{
    // Two hundred of the Japanese characters used here total 600 UTF-8 bytes, exceeding the protocol limit.
    private static readonly string LongTranscript = new('あ', 200);

    // Keep each sentence within 60 characters so sentence-assembler behavior does not change.
    private static readonly string[] Sentences =
    [
        "[happy]こんにちは、スタックちゃんです。",
        "[neutral]きょうは良い天気ですね。",
    ];

    private GatewayFactory _factory = null!;

    private string? _contentType;

    private byte[] _body = [];

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _factory = new GatewayFactory();

        // Trigger conversation.text truncation with a recognition result over 512 bytes.
        _factory.SpeechToText.Result = LongTranscript;

        _factory.Agent.Fragments = Sentences;

        _factory.TextToSpeech.Result = new PcmAudio(
            new short[2500],
            PcmAudio.CanonicalSampleRate,
            PcmAudio.CanonicalChannels);

        using var client = _factory.CreateClient();
        using var request = DeviceRequest.Speech(conversation: "conv-regression");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        _contentType = response.Content.Headers.ContentType?.ToString();
        _body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public void 複数の境界条件を含む会話でも_プロトコル契約を満たす()
    {
        // Conformance checks also include the 8,192-byte SSE line and 512-byte text limits.
        var violations = ConformanceChecks.Run(_contentType, _body);

        violations.ShouldBeEmpty(
            "違反: " + string.Join(" / ", violations.Select(violation => violation.ToString())));
    }

    [Fact]
    public void 長い音声認識結果は_512_バイト以内へ切り詰める()
    {
        var text = SseWire.Events(_body)
            .First(wire => wire.Name == "conversation.text")
            .Payload.GetProperty("text").GetString().ShouldNotBeNull();

        System.Text.Encoding.UTF8.GetByteCount(text).ShouldBeLessThanOrEqualTo(DeviceLimits.MaxTextBytes);

        // Preserve UTF-8 character boundaries so the device can decode the string.
        text.ShouldNotContain("�");
        text.ShouldBe(new string('あ', 170));
    }

    [Fact]
    public void 表情マーカーは_文ごとにプロトコルへ反映する()
    {
        // Convert each expression enum value to its corresponding protocol marker.
        var texts = SseWire.Events(_body)
            .Where(wire => wire.Name == "reply.audio" &&
                           wire.Payload.TryGetProperty("text", out _))
            .Select(wire => wire.Payload.GetProperty("text").GetString())
            .ToList();

        texts.Count.ShouldBe(Sentences.Length);
        texts[0].ShouldStartWith("[happy]");
        texts[1].ShouldStartWith("[neutral]");
    }

    [Fact]
    public void SSE_の各行は_プロトコルの上限を超えない()
    {
        // Check limits against actual UTF-8 bytes containing Base64 audio and text.
        var longest = SseWire.Parse(_body).Max(record => record.ByteLength);

        longest.ShouldBeLessThanOrEqualTo(DeviceLimits.MaxEventBytes);
    }

    [Fact]
    public void シーケンス番号は_0_から連続し_最後のイベントだけが_last_になる()
    {
        var audio = SseWire.Events(_body)
            .Where(wire => wire.Name == "reply.audio")
            .ToList();

        audio.ShouldNotBeEmpty();

        audio.Select(wire => wire.Payload.GetProperty("seq").GetInt64())
            .ShouldBe(Enumerable.Range(0, audio.Count).Select(index => (long)index));

        audio.Count(wire => wire.Payload.GetProperty("last").GetBoolean()).ShouldBe(1);
        audio[^1].Payload.GetProperty("last").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void 会話は_completed_イベントで終了する()
    {
        var events = SseWire.Events(_body);

        events[^1].Name.ShouldBe("conversation.finished");
        events[^1].Payload.GetProperty("reason").GetString().ShouldBe("completed");
    }
}
