using System.Net;
using System.Text;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Sse;
using Kkdev92.StackChan.Gateway.TestKit;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Tests;

/// <summary>Verifies the HTTP and SSE contract of the Atoms3R conversation endpoint.</summary>
public sealed class ConverseWireTests
{
    private static PcmAudio Audio(int samples) =>
        new(new short[samples], PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);

    [Fact]
    public async Task 正常な会話は_200_の_event_stream_で応答する()
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new TranscriptAvailable("こんにちは"),
            new ReplyAudioAvailable("はい。", SpeechExpression.Happy, Audio(100)),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString()
            .ShouldBe("text/event-stream; charset=utf-8");
        response.Headers.CacheControl?.ToString().ShouldBe("no-store");
    }

    [Fact]
    public async Task デバイスIDと会話IDを_ターン要求へ引き渡す()
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.Add(new TurnCompleted(TurnCompletionReason.Completed));

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(device: "atoms3r-aabbccddeeff", conversation: "conv-42"),
            TestContext.Current.CancellationToken);

        var request = host.Runtime.Requests.ShouldHaveSingleItem();
        request.SessionId.Value.ShouldBe("atoms3r-aabbccddeeff");
        request.Device.DeviceId.Value.ShouldBe("atoms3r-aabbccddeeff");
        request.Device.BootId.ShouldBe("BOOT00000000000000000000AB");
        request.Device.ConversationId.ShouldBe("conv-42");
    }

    [Fact]
    public async Task 音声認識結果は_conversation_text_イベントとして送信する()
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new TranscriptAvailable("こんにちは"),
            new ReplyAudioAvailable("はい。", SpeechExpression.Happy, Audio(10)),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);
        var events = SseWire.Events(body);

        var text = events.First(wire => wire.Name == "conversation.text");
        text.Payload.GetProperty("text").GetString().ShouldBe("こんにちは");
        text.Payload.GetProperty("final").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task 音声認識結果は_UTF8_で_512_バイト以内へ切り詰める()
    {
        // The firmware JSON decoder cannot decode strings over 512 bytes and discards the entire
        // conversation.text event.
        await using var host = await ProtocolHost.StartAsync();

        // The Japanese character used here is three UTF-8 bytes, so 200 characters total 600 bytes.
        var spoken = new string('あ', 200);

        host.Runtime.Events.AddRange([
            new TranscriptAvailable(spoken),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);
        var events = SseWire.Events(body);

        var text = events.First(wire => wire.Name == "conversation.text")
            .Payload.GetProperty("text").GetString().ShouldNotBeNull();

        // At three bytes each, 170 characters total 510 bytes, the closest character boundary below the limit.
        Encoding.UTF8.GetByteCount(text).ShouldBeLessThanOrEqualTo(512);
        text.ShouldBe(new string('あ', 170));

        // Preserve UTF-8 character boundaries.
        text.ShouldNotContain("�");

        // Also confirm that the complete response passes conformance checks after truncation.
        ConformanceChecks.Run(ConformanceChecks.ExpectedContentType, body).ShouldBeEmpty();
    }

    [Fact]
    public async Task 上限以内の音声認識結果は_切り詰めずに送信する()
    {
        // Combine 170 three-byte characters and one ASCII character to reach 511 bytes.
        await using var host = await ProtocolHost.StartAsync();

        var spoken = new string('あ', 170) + "a";  // 510 + 1 = 511 bytes

        host.Runtime.Events.AddRange([
            new TranscriptAvailable(spoken),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);

        SseWire.Events(body).First(wire => wire.Name == "conversation.text")
            .Payload.GetProperty("text").GetString().ShouldBe(spoken);
    }

    [Fact]
    public async Task 音声認識結果を_サロゲートペアの途中で切らない()
    {
        // Place a four-byte emoji across the limit boundary.
        await using var host = await ProtocolHost.StartAsync();

        // Adding a four-byte emoji to a 510-byte string produces 514 bytes.
        var spoken = new string('あ', 170) + "😀";

        host.Runtime.Events.AddRange([
            new TranscriptAvailable(spoken),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);

        var text = SseWire.Events(body).First(wire => wire.Name == "conversation.text")
            .Payload.GetProperty("text").GetString().ShouldNotBeNull();

        // Remove the emoji as one code point when it does not fit.
        text.ShouldBe(new string('あ', 170));
        text.ShouldNotContain("�");
        char.IsHighSurrogate(text[^1]).ShouldBeFalse();
    }

    [Fact]
    public async Task PCMは_4096_バイト単位で分割し_連続するシーケンス番号を付ける()
    {
        await using var host = await ProtocolHost.StartAsync();

        // 2,048 samples of 16-bit PCM equal 4,096 bytes.
        host.Runtime.Events.AddRange([
            new ReplyAudioAvailable("ながいぶん。", SpeechExpression.Happy, Audio(5000)),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);
        var audio = SseWire.Events(body).Where(wire => wire.Name == "reply.audio").ToList();

        // Split 5,000 samples across three audio events and add one terminal event.
        audio.Count.ShouldBe(4);
        audio.Select(wire => wire.Payload.GetProperty("seq").GetInt64()).ShouldBe([0, 1, 2, 3]);

        // Include sentence text only in the first audio event.
        audio[0].Payload.TryGetProperty("text", out _).ShouldBeTrue();
        audio.Skip(1).ShouldAllBe(wire => wire.Payload.GetRawText().Contains("text") == false);

        // Set last=true only on the final event, which has no PCM.
        audio[^1].Payload.GetProperty("last").GetBoolean().ShouldBeTrue();
        audio[^1].Payload.GetProperty("pcm").GetString().ShouldBe("");
        audio.Take(3).ShouldAllBe(wire => !wire.Payload.GetProperty("last").GetBoolean());
    }

    [Fact]
    public async Task 音声合成結果が無音でも_テキストを送信する()
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new ReplyAudioAvailable("よめませんでした。", SpeechExpression.Sad, PcmAudio.Silence),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);
        var audio = SseWire.Events(body).Where(wire => wire.Name == "reply.audio").ToList();

        audio.Count.ShouldBe(2);
        audio[0].Payload.GetProperty("text").GetString().ShouldBe("[sad]よめませんでした。");
        audio[0].Payload.GetProperty("pcm").GetString().ShouldBe("");
        audio[0].Payload.GetProperty("last").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData(SpeechExpression.Neutral, "[neutral]はい。")]
    [InlineData(SpeechExpression.Happy, "[happy]はい。")]
    [InlineData(SpeechExpression.Sad, "[sad]はい。")]
    [InlineData(SpeechExpression.Doubt, "[doubt]はい。")]
    [InlineData(SpeechExpression.Sleepy, "[sleepy]はい。")]
    [InlineData(SpeechExpression.Angry, "[angry]はい。")]
    public async Task 表情は_プロトコルで定義されたマーカーとして送信する(
        SpeechExpression expression,
        string expected)
    {
        // The firmware interprets only recognized forms as expression markers.
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new ReplyAudioAvailable("はい。", expression, Audio(10)),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);
        var audio = SseWire.Events(body).First(wire => wire.Name == "reply.audio");

        audio.Payload.GetProperty("text").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task ターン失敗時は_error_raised_の後に_finished_failed_を送信する()
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new TurnFailed(new GatewayError(GatewayErrorCode.Unavailable, "speech recognition failed", true)),
            new TurnCompleted(TurnCompletionReason.Failed),
        ]);

        var body = await ReadBodyAsync(host);
        var events = SseWire.Events(body);

        var error = events.First(wire => wire.Name == "error.raised");
        error.Payload.GetProperty("code").GetString().ShouldBe("unavailable");
        error.Payload.GetProperty("message").GetString().ShouldBe("speech recognition failed");
        error.Payload.GetProperty("retryable").GetBoolean().ShouldBeTrue();

        events[^1].Name.ShouldBe("conversation.finished");
        events[^1].Payload.GetProperty("reason").GetString().ShouldBe("failed");

        // Do not send an audio terminal event when no audio event was started.
        events.ShouldAllBe(wire => wire.Name != "reply.audio");
    }

    [Fact]
    public async Task 音声イベント開始後の失敗は_音声を閉じてから_finished_failed_を送信する()
    {
        // The firmware finalizes pending captions when it receives last=true.
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new ReplyAudioAvailable("字幕だけの文", SpeechExpression.Happy, PcmAudio.Silence),
            new TurnFailed(new GatewayError(GatewayErrorCode.Unavailable, "tts down", true)),
            new TurnCompleted(TurnCompletionReason.Failed),
        ]);

        var body = await ReadBodyAsync(host);
        var events = SseWire.Events(body);
        var audio = events.Where(wire => wire.Name == "reply.audio").ToList();

        // Send an empty terminal event after the event containing text.
        audio.Count.ShouldBe(2);
        audio[0].Payload.GetProperty("text").GetString().ShouldBe("[happy]字幕だけの文");
        audio[0].Payload.GetProperty("last").GetBoolean().ShouldBeFalse();
        audio[1].Payload.GetProperty("last").GetBoolean().ShouldBeTrue();
        audio[1].Payload.GetProperty("seq").GetInt32().ShouldBe(1);

        // Close the audio stream before conversation.finished.
        events[^1].Name.ShouldBe("conversation.finished");
        events[^1].Payload.GetProperty("reason").GetString().ShouldBe("failed");

        ConformanceChecks.Run(ConformanceChecks.ExpectedContentType, body).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(GatewayErrorCode.Unavailable, "unavailable")]
    [InlineData(GatewayErrorCode.Timeout, "timeout")]
    [InlineData(GatewayErrorCode.Busy, "busy")]
    [InlineData(GatewayErrorCode.Cancelled, "cancelled")]
    [InlineData(GatewayErrorCode.Internal, "internal")]
    public async Task エラーコードを_プロトコルで定義された表記へ変換する(
        GatewayErrorCode code,
        string expected)
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new TurnFailed(new GatewayError(code, "detail", false)),
            new TurnCompleted(TurnCompletionReason.Failed),
        ]);

        var body = await ReadBodyAsync(host);
        var error = SseWire.Events(body).First(wire => wire.Name == "error.raised");

        error.Payload.GetProperty("code").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task 認証トークンが一致しなければ_401_を返してターンを開始しない()
    {
        await using var host = await ProtocolHost.StartAsync(token: "0123456789abcdef0123456789abcdef");

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(token: "wrong"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("authentication failed");
        host.Runtime.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task 認証トークンが一致すれば_ターンを開始する()
    {
        const string token = "0123456789abcdef0123456789abcdef";
        await using var host = await ProtocolHost.StartAsync(token: token);
        host.Runtime.Events.Add(new TurnCompleted(TurnCompletionReason.Completed));

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(token: token), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Runtime.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task デバイスIDヘッダーが無ければ_400_を返す()
    {
        await using var host = await ProtocolHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/converse")
        {
            Content = new ByteArrayContent(WavFactory.Wav(new byte[3200], 16000, 1)),
        };
        request.Content.Headers.ContentType = new("audio/wav");

        using var response = await host.Client.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        host.Runtime.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task 応答テキストも_UTF8_で_512_バイト以内へ切り詰める()
    {
        // The sentence assembler normally splits at 60 characters first, but the protocol layer applies
        // its own limit. Otherwise the firmware discards an oversized event and leaves a sequence gap.
        await using var host = await ProtocolHost.StartAsync();

        var longSentence = new string('あ', 400);   // 1200 bytes in UTF-8

        host.Runtime.Events.AddRange([
            new ReplyAudioAvailable(longSentence, SpeechExpression.Happy, Audio(100)),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var body = await ReadBodyAsync(host);

        var text = SseWire.Events(body).First(wire => wire.Name == "reply.audio")
            .Payload.GetProperty("text").GetString().ShouldNotBeNull();

        Encoding.UTF8.GetByteCount(text).ShouldBeLessThanOrEqualTo(512);

        // Preserve UTF-8 character boundaries and the leading expression marker.
        text.ShouldNotContain("�");
        text.ShouldStartWith("[happy]");

        // Also confirm that the complete response passes conformance checks after truncation.
        ConformanceChecks.Run(ConformanceChecks.ExpectedContentType, body).ShouldBeEmpty();
    }

    [Fact]
    public async Task SSEの行が_8192_バイトを超える場合は_送信前に失敗する()
    {
        // Detect an oversized event before writing because the firmware discards events over its line buffer.
        await using var sse = await EnvelopeSse.StartAsync(
            new DefaultHttpContext().Response,
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        var tooLong = new string('x', EnvelopeSse.MaxEventBytes);

        Should.Throw<InvalidOperationException>(
            () => sse.SendEvent("reply.audio", json => json.WriteString("pcm", tooLong)));
    }

    [Fact]
    public async Task Content_Lengthが要求上限を超えていれば_413_を返す()
    {
        await using var host = await ProtocolHost.StartAsync(maxRequestBodyBytes: 4096);

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(wav: WavFactory.Wav(new byte[8192], 16000, 1)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        host.Runtime.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Content_Lengthが無くても_要求上限を超えたら_413_を返す()
    {
        // Limit bytes while reading, independent of host configuration.
        await using var host = await ProtocolHost.StartAsync(maxRequestBodyBytes: 4096);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/converse")
        {
            Content = new StreamContent(new UnknownLengthStream(new byte[16384])),
        };
        request.Content.Headers.ContentType = new("audio/wav");
        request.Headers.Add("X-StackChan-Device", "atoms3r-aabbccddeeff");

        request.Content.Headers.ContentLength.ShouldBeNull("リクエストに Content-Length が設定されています。");

        using var response = await host.Client.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        host.Runtime.Requests.ShouldBeEmpty();
    }

    /// <summary>A request-body stream whose Content-Length cannot be determined.</summary>
    private sealed class UnknownLengthStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task WAVヘッダーに満たない本文なら_400_を返す()
    {
        await using var host = await ProtocolHost.StartAsync();

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(wav: new byte[10]), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("wav is required");
        host.Runtime.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task JSONの発話テキストを_ターン要求へ引き渡す()
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.Add(new TurnCompleted(TurnCompletionReason.Completed));

        using var request = DeviceRequest.Text("おはよう");

        using var response = await host.Client.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Runtime.Requests.ShouldHaveSingleItem().UserText.ShouldBe("おはよう");
    }

    [Fact]
    public async Task JSON文字列が不正な_UTF8_なら_400_を返す()
    {
        // Treat an undecodable string as a client error even when the JSON structure is valid.
        await using var host = await ProtocolHost.StartAsync();

        // {"text":"<0x82 0x4f>"}: a byte sequence that cannot be decoded as UTF-8.
        var broken = new List<byte>();
        broken.AddRange("{\"text\":\""u8);
        broken.AddRange([0x82, 0x4f]);
        broken.AddRange("\"}"u8);

        using var content = new ByteArrayContent([.. broken]);
        content.Headers.ContentType = new("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/converse")
        {
            Content = content,
        };
        request.Headers.Add("X-StackChan-Device", "atoms3r-001122334455");

        using var response = await host.Client.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("text is required");
    }

    [Fact]
    public async Task JSON本文に_text_が無ければ_400_を返す()
    {
        await using var host = await ProtocolHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/converse")
        {
            Content = new StringContent("""{"speak":"おはよう"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-StackChan-Device", "atoms3r-001122334455");

        using var response = await host.Client.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("text is required");
    }

    [Fact]
    public async Task キャンセル時は_finished_cancelled_で終了する()
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.AddRange([
            new TranscriptAvailable("こんにちは"),
            new TurnFailed(new GatewayError(GatewayErrorCode.Cancelled, "cancelled", false)),
            new TurnCompleted(TurnCompletionReason.Cancelled),
        ]);

        var body = await ReadBodyAsync(host);
        var events = SseWire.Events(body);

        // Report cancellation as a completion event while the connection remains active.
        events[^1].Name.ShouldBe("conversation.finished");
        events[^1].Payload.GetProperty("reason").GetString().ShouldBe("cancelled");
    }

    [Fact(Timeout = 30_000)]
    public async Task 応答ヘッダーは_最初のターンイベントを待たずに送信する()
    {
        // Clients must observe response start before slow recognition or response generation completes.
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.BlockBeforeFirstEvent = new TaskCompletionSource();
        host.Runtime.Events.Add(new TurnCompleted(TurnCompletionReason.Completed));

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(),
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        // Headers remain available while the turn runtime is blocked.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString()
            .ShouldBe("text/event-stream; charset=utf-8");

        host.Runtime.BlockBeforeFirstEvent.SetResult();
        _ = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30_000)]
    public async Task ターンイベントを待つ間は_keep_alive_コメントを送信する()
    {
        await using var host = await ProtocolHost.StartAsync(keepAliveIntervalSeconds: 1);
        host.Runtime.BlockBeforeFirstEvent = new TaskCompletionSource();
        host.Runtime.Events.Add(new TurnCompleted(TurnCompletionReason.Completed));

        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(),
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);

        var buffer = new byte[512];
        var seen = new StringBuilder();

        // Keep the connection open after conversation.started while waiting for the next turn event.
        while (!seen.ToString().Contains(": keep-alive", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
            read.ShouldBeGreaterThan(0);
            seen.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }

        // Complete the pending request and read the full response before disposing the host.
        host.Runtime.BlockBeforeFirstEvent.SetResult();
        await DrainAsync(stream);
    }

    private static async Task DrainAsync(Stream stream)
    {
        var buffer = new byte[1024];

        while (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0)
        {
        }
    }

    private static async Task<byte[]> ReadBodyAsync(ProtocolHost host)
    {
        using var response = await host.Client.SendAsync(
            DeviceRequest.Speech(), TestContext.Current.CancellationToken);

        return await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }
}
