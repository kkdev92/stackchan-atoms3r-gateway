using System.Buffers.Binary;
using System.Net;
using System.Text;
using Kkdev92.StackChan.Gateway.Abstractions;
using RichardSzalay.MockHttp;
using Shouldly;
using Xunit;

namespace StackChan.Provider.PiperPlus.Tests;

/// <summary>
/// Verifies piper-plus requests and conversion rules for returned WAV data.
/// </summary>
/// <remarks>
/// Voice models vary in sample rate and channel count, so output must be converted to the device's
/// 16 kHz mono format.
/// </remarks>
public sealed class PiperPlusTextToSpeechTests
{
    private static readonly PiperPlusOptions Options = new()
    {
        Endpoint = "http://127.0.0.1:5000",
        Path = "/tts_live.wav",
        LengthScale = 1.0,
        Character = "",
        TimeoutSeconds = 30,
    };

    [Fact]
    public async Task tts_live_wav_へ_text_と_length_scale_を送る()
    {
        using var handler = new MockHttpMessageHandler();
        Uri? requested = null;

        handler.When(HttpMethod.Get, "*")
            .With(request =>
            {
                requested = request.RequestUri;
                return true;
            })
            .Respond(new ByteArrayContent(Wav(16000, 1, 100)));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        await tts.SynthesizeAsync("こんにちは。", TestContext.Current.CancellationToken);

        requested.ShouldNotBeNull();
        requested.AbsolutePath.ShouldBe("/tts_live.wav");
        requested.Host.ShouldBe("127.0.0.1");
        requested.Port.ShouldBe(5000);

        // URL-encode spoken text and send the configured length_scale.
        requested.Query.ShouldContain("text=" + Uri.EscapeDataString("こんにちは。"));
        requested.Query.ShouldContain("length_scale=1.00");
        requested.Query.ShouldNotContain("character");
    }

    [Fact]
    public async Task 音声モデル名がある場合だけ_character_を付ける()
    {
        using var handler = new MockHttpMessageHandler();
        Uri? requested = null;

        handler.When(HttpMethod.Get, "*")
            .With(request =>
            {
                requested = request.RequestUri;
                return true;
            })
            .Respond(new ByteArrayContent(Wav(16000, 1, 100)));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(
            () => client,
            new PiperPlusOptions { Character = "ずんだもん", LengthScale = 1.2 });

        await tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken);

        requested!.Query.ShouldContain("character=" + Uri.EscapeDataString("ずんだもん"));
        requested.Query.ShouldContain("length_scale=1.20");
    }

    [Fact]
    public async Task 応答音声が_16kHz_モノラルなら_サンプルをそのまま返す()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond(new ByteArrayContent(Wav(16000, 1, 320)));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        var audio = await tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken);

        audio.SampleRate.ShouldBe(16000);
        audio.Channels.ShouldBe(1);
        audio.Samples.Length.ShouldBe(320);
        audio.IsCanonical.ShouldBeTrue();
    }

    [Fact]
    public async Task 高いサンプルレートは_16kHz_へ変換する()
    {
        // Convert a 22.05 kHz voice model to the device's 16 kHz format.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond(new ByteArrayContent(Wav(22050, 1, 2205)));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        var audio = await tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken);

        audio.SampleRate.ShouldBe(16000);
        audio.Channels.ShouldBe(1);
        audio.Samples.Length.ShouldBe(2205 * 16000 / 22050);
        audio.IsCanonical.ShouldBeTrue();
    }

    [Fact]
    public async Task ステレオ音声は_モノラルへダウンミックスする()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond(new ByteArrayContent(Wav(16000, 2, 200)));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        var audio = await tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken);

        audio.Channels.ShouldBe(1);

        // Combine the left and right channels of 200 frames into 200 mono samples.
        audio.Samples.Length.ShouldBe(200);
    }

    [Fact]
    public async Task fmt_と_data_の間に未知のチャンクがあっても読み取れる()
    {
        // Support WAV data with a LIST chunk without assuming a fixed 44-byte header.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond(new ByteArrayContent(WavWithListChunk(16000, 1, 64)));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        var audio = await tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken);

        audio.Samples.Length.ShouldBe(64);
    }

    [Fact]
    public async Task 不正な_WAV_は_ProviderException_として扱う()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond(new ByteArrayContent(Encoding.ASCII.GetBytes("not a wav at all")));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        var exception = await Should.ThrowAsync<ProviderException>(
            () => tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Unavailable);
        exception.Message.ShouldBe("speech synthesis failed");
        exception.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task HTTP_エラーは_ProviderException_として扱う()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond(HttpStatusCode.ServiceUnavailable);

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        var exception = await Should.ThrowAsync<ProviderException>(
            () => tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Unavailable);
        exception.Retryable.ShouldBeTrue();
        exception.Message.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task タイムアウトは_timeout_エラーとして扱う()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(
            () => client,
            new PiperPlusOptions { TimeoutSeconds = 1 });

        var exception = await Should.ThrowAsync<ProviderException>(
            () => tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Timeout);
    }

    [Fact]
    public async Task 呼び出し元からのキャンセルは_そのまま伝播する()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        using var cancellation = new CancellationTokenSource();
        var running = tts.SynthesizeAsync("はい。", cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => running);
    }

    /// <summary>Creates a test WAV whose sample values increase monotonically.</summary>
    private static byte[] Wav(int sampleRate, int channels, int frames)
    {
        var pcm = new byte[frames * channels * 2];

        for (var i = 0; i < frames * channels; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), (short)(i % 3000));
        }

        return Riff(sampleRate, channels, pcm, includeList: false);
    }

    private static byte[] WavWithListChunk(int sampleRate, int channels, int frames)
    {
        var pcm = new byte[frames * channels * 2];
        return Riff(sampleRate, channels, pcm, includeList: true);
    }

    private static byte[] Riff(int sampleRate, int channels, byte[] pcm, bool includeList)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII);

        var listSize = includeList ? 12 : 0;

        writer.Write("RIFF"u8);
        writer.Write(36 + listSize + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);

        if (includeList)
        {
            writer.Write("LIST"u8);
            writer.Write(4);
            writer.Write("INFO"u8);
        }

        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();

        return buffer.ToArray();
    }

    [Fact]
    public async Task 上限を超える応答は_再試行不能なエラーとして扱う()
    {
        // Do not trust Content-Length; enforce the limit while reading to bound memory usage.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond(
            "audio/wav", new MemoryStream(new byte[64 * 1024]));

        using var client = handler.ToHttpClient();
        var options = new PiperPlusOptions
        {
            Endpoint = Options.Endpoint,
            Path = Options.Path,
            LengthScale = Options.LengthScale,
            Character = Options.Character,
            TimeoutSeconds = Options.TimeoutSeconds,
            MaxResponseBytes = 4096,
        };

        var tts = new PiperPlusTextToSpeech(() => client, options);

        var exception = await Should.ThrowAsync<ProviderException>(
            () => tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Unavailable);

        // An oversized response will not improve on retry, so mark it non-retryable.
        exception.Retryable.ShouldBeFalse();

        // Do not include the endpoint or actual size in error responses.
        exception.Message.ShouldBe("speech synthesis failed");
    }

    [Fact]
    public async Task 上限以内の応答は_正常に読み取れる()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond("audio/wav", new MemoryStream(Wav(16000, 1, 400)));

        using var client = handler.ToHttpClient();
        var tts = new PiperPlusTextToSpeech(() => client, Options);

        var pcm = await tts.SynthesizeAsync("はい。", TestContext.Current.CancellationToken);

        pcm.Samples.Length.ShouldBeGreaterThan(0);
    }
}
