using System.Net;
using System.Text;
using Kkdev92.StackChan.Gateway.Abstractions;
using RichardSzalay.MockHttp;
using Shouldly;
using Xunit;

namespace StackChan.Provider.WhisperCpp.Tests;

/// <summary>
/// Verifies whisper.cpp requests and handling of recognition results.
/// </summary>
/// <remarks>
/// Fixes multipart field names and defaults to preserve compatibility with existing whisper-server deployments.
/// </remarks>
public sealed class WhisperCppSpeechToTextTests
{
    private static readonly WhisperCppOptions Options = new()
    {
        Endpoint = "http://127.0.0.1:8081",
        Path = "/inference",
        Language = "ja",
        TimeoutSeconds = 30,
    };

    [Fact]
    public async Task inference_へ_multipart_要求を送る()
    {
        using var handler = new MockHttpMessageHandler();
        var capture = new RequestCapture();

        handler.When(HttpMethod.Post, "http://127.0.0.1:8081/inference")
            .With(capture.Record)
            .Respond("application/json", """{"text":"こんにちは"}""");

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("こんにちは");

        capture.ContentType.ShouldStartWith("multipart/form-data");

        // Preserve field names and defaults expected by whisper-server.
        capture.Text.ShouldContain("name=temperature");
        capture.Text.ShouldContain("0.0");
        capture.Text.ShouldContain("name=temperature_inc");
        capture.Text.ShouldContain("0.2");
        capture.Text.ShouldContain("name=response_format");
        capture.Text.ShouldContain("json");
        capture.Text.ShouldContain("name=language");
        capture.Text.ShouldContain("ja");
        capture.Text.ShouldContain("name=file");
        capture.Text.ShouldContain("filename=speak.wav");
        capture.Text.ShouldContain("Content-Type: application/octet-stream");
    }

    [Fact]
    public async Task file_には_入力音声から組み立てた_WAV_を送る()
    {
        using var handler = new MockHttpMessageHandler();
        var capture = new RequestCapture();

        handler.When(HttpMethod.Post, "*")
            .With(capture.Record)
            .Respond("application/json", """{"text":"はい"}""");

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        // Use identifiable samples to verify that the WAV data section is preserved.
        var samples = new short[160];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(i + 1);
        }

        await stt.TranscribeAsync(
            new PcmAudio(samples, 16000, 1), TestContext.Current.CancellationToken);

        // A 44-byte WAV header is followed by s16le samples.
        var header = IndexOf(capture.Body, Encoding.ASCII.GetBytes("RIFF"));
        header.ShouldBeGreaterThan(0, "multipart 本文に WAV ヘッダーが見つかりません。");

        var wav = capture.Body.AsSpan(header);
        Encoding.ASCII.GetString(wav[8..12]).ShouldBe("WAVE");
        Encoding.ASCII.GetString(wav[36..40]).ShouldBe("data");
        BitConverter.ToUInt16(wav[22..24]).ShouldBe((ushort)1);      // mono
        BitConverter.ToUInt32(wav[24..28]).ShouldBe(16000u);         // 16kHz
        BitConverter.ToUInt16(wav[34..36]).ShouldBe((ushort)16);     // 16bit
        BitConverter.ToUInt32(wav[40..44]).ShouldBe((uint)(samples.Length * 2));
        BitConverter.ToInt16(wav[44..46]).ShouldBe((short)1);
        BitConverter.ToInt16(wav[46..48]).ShouldBe((short)2);
    }

    [Fact]
    public async Task 言語が空なら_language_フィールドを送らない()
    {
        using var handler = new MockHttpMessageHandler();
        var capture = new RequestCapture();

        handler.When(HttpMethod.Post, "*")
            .With(capture.Record)
            .Respond("application/json", """{"text":"はい"}""");

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(
            () => client,
            new WhisperCppOptions { Language = "" });

        await stt.TranscribeAsync(Wav(), TestContext.Current.CancellationToken);

        capture.Text.ShouldNotContain("name=language");
    }

    [Fact]
    public async Task 言語信頼度を検査する場合は_verbose_json_を要求する()
    {
        using var handler = new MockHttpMessageHandler();
        var capture = new RequestCapture();

        handler.When(HttpMethod.Post, "*")
            .With(capture.Record)
            .Respond("application/json", Verbose("こんにちは", ja: 0.99));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("こんにちは");
        capture.Text.ShouldContain("verbose_json");
    }

    [Fact]
    public async Task 指定言語の信頼度が低ければ_空の認識結果を返す()
    {
        // Non-speech can have a low no_speech_prob, so also evaluate confidence in the selected language.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond("application/json", Verbose("ご飯を食べます。", ja: 0.08));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBeEmpty();
    }

    [Fact]
    public async Task 言語信頼度が閾値以上なら_認識結果を受け入れる()
    {
        // Accept or reject based on language confidence returned by whisper.cpp, not on the input audio itself.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond("application/json", Verbose("こんにちは、いい天気ですね", ja: 0.928));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("こんにちは、いい天気ですね");
    }

    [Theory]
    [InlineData(0.0, "json")]
    [InlineData(0.5, "verbose_json")]
    public async Task 言語信頼度の閾値に応じて_応答形式と採否判定を切り替える(double minimum, string expected)
    {
        using var handler = new MockHttpMessageHandler();
        var capture = new RequestCapture();

        handler.When(HttpMethod.Post, "*")
            .With(capture.Record)
            .Respond("application/json", Verbose("ご飯を食べます。", ja: 0.08));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(
            () => client,
            new WhisperCppOptions { Language = "ja", MinLanguageProbability = minimum });

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        capture.Text.ShouldContain(expected);
        transcript.Text.ShouldBe(minimum == 0 ? "ご飯を食べます。" : "");
    }

    [Fact]
    public async Task 言語を自動判定する場合は_判定結果の信頼度を使う()
    {
        // Check confidence in automatic detection to exclude noise-induced recognition.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond("application/json", Verbose("ご飯を食べます。", ja: 0.08, detected: 0.41));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(
            () => client,
            new WhisperCppOptions { Language = "auto", MinLanguageProbability = 0.5 });

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBeEmpty();
    }

    [Fact]
    public async Task 言語を自動判定する場合は_日本語以外の音声も受け入れる()
    {
        // Automatic detection uses confidence in the detected language, not specifically Japanese.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond("application/json", Verbose("Hello, how are you?", ja: 0.0012, detected: 0.988));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(
            () => client,
            new WhisperCppOptions { Language = "auto", MinLanguageProbability = 0.5 });

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("Hello, how are you?");
    }

    [Fact]
    public async Task 言語を指定した場合は_他言語の音声を除外する()
    {
        // Reject a result when confidence in the selected language is below the threshold.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond("application/json", Verbose("Hello, how are you?", ja: 0.0012, detected: 0.988));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBeEmpty();
    }

    /// <summary>Builds only the required fields of a whisper-server <c>verbose_json</c> response.</summary>
    private static string Verbose(string text, double ja, double? detected = null) =>
        $@"{{
          ""task"": ""transcribe"",
          ""language"": ""japanese"",
          ""duration"": 3.0,
          ""text"": ""{text}"",
          ""segments"": [
            {{ ""id"": 0, ""text"": ""{text}"", ""avg_logprob"": -0.09, ""no_speech_prob"": 1.39e-06 }}
          ],
          ""detected_language"": ""japanese"",
          ""detected_language_probability"": {detected ?? ja},
          ""language_probabilities"": {{ ""en"": 0.35, ""ja"": {ja} }}
        }}";

    [Fact]
    public async Task 認識結果の前後の空白を除去する()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond("application/json", """{"text":"  こんにちは  "}""");

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("こんにちは");
    }

    [Fact]
    public async Task text_が無ければ_空の認識結果を返す()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*").Respond("application/json", """{"other":1}""");

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var transcript = await stt.TranscribeAsync(
            Wav(), TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("");
    }

    /// <summary>
    /// Converts HTTP errors to ProviderException and determines retryability from the status code.
    /// </summary>
    /// <remarks>
    /// Ordinary 4xx responses are not retried because the request or configuration must change.
    /// Status codes 408 and 429 are retryable transient server states.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task HTTP_エラーを_再試行可否つきの_ProviderException_へ変換する(HttpStatusCode status, bool retryable)
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*").Respond(status);

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var exception = await Should.ThrowAsync<ProviderException>(
            () => stt.TranscribeAsync(Wav(), TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Unavailable);
        exception.Message.ShouldBe("speech recognition failed");
        exception.Retryable.ShouldBe(retryable);

        // Do not include the endpoint in the error response.
        exception.Message.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task 連続して失敗したら_サーキットを開いて要求を遮断する()
    {
        // Verify by HTTP request count that a failing service is not called continuously.
        using var handler = new MockHttpMessageHandler();
        var attempts = 0;
        handler.When(HttpMethod.Post, "*").Respond(_ =>
        {
            attempts++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        for (var index = 0; index < 6; index++)
        {
            await Should.ThrowAsync<ProviderException>(
                () => stt.TranscribeAsync(Wav(), TestContext.Current.CancellationToken));
        }

        // The default threshold is three; do not send a fourth or later HTTP request.
        attempts.ShouldBe(3, $"HTTP リクエストが {attempts} 回送信されました。");
    }

    [Fact]
    public async Task 再試行不能な失敗では_サーキットを開かない()
    {
        // Return the original cause for configuration-related 4xx responses instead of replacing it with a circuit failure.
        using var handler = new MockHttpMessageHandler();
        var attempts = 0;
        handler.When(HttpMethod.Post, "*").Respond(_ =>
        {
            attempts++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        for (var index = 0; index < 6; index++)
        {
            await Should.ThrowAsync<ProviderException>(
                () => stt.TranscribeAsync(Wav(), TestContext.Current.CancellationToken));
        }

        attempts.ShouldBe(6);
    }

    [Fact]
    public async Task 不正な_JSON_は_ProviderException_として扱う()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*").Respond("application/json", "{ not json");

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var exception = await Should.ThrowAsync<ProviderException>(
            () => stt.TranscribeAsync(Wav(), TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Unavailable);
        exception.Message.ShouldBe("speech recognition failed");
    }

    [Fact]
    public async Task 接続できない場合は_ProviderException_として扱う()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*").Throw(new HttpRequestException("no route"));

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        var exception = await Should.ThrowAsync<ProviderException>(
            () => stt.TranscribeAsync(Wav(), TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Unavailable);
        exception.Retryable.ShouldBeTrue();
    }

    [Fact]
    public async Task タイムアウトは_timeout_エラーとして扱う()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(
            () => client,
            new WhisperCppOptions { TimeoutSeconds = 1 });

        var exception = await Should.ThrowAsync<ProviderException>(
            () => stt.TranscribeAsync(Wav(), TestContext.Current.CancellationToken));

        exception.Code.ShouldBe(GatewayErrorCode.Timeout);
        exception.Retryable.ShouldBeTrue();
    }

    [Fact]
    public async Task 呼び出し元からのキャンセルは_そのまま伝播する()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, "*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var client = handler.ToHttpClient();
        var stt = new WhisperCppSpeechToText(() => client, Options);

        using var cancellation = new CancellationTokenSource();
        var running = stt.TranscribeAsync(Wav(), cancellation.Token);
        await cancellation.CancelAsync();

        // Do not convert cancellation to ProviderException, allowing the runtime to treat it as Cancelled.
        await Should.ThrowAsync<OperationCanceledException>(() => running);
    }

    private static PcmAudio Wav() => new(new short[160], 16000, 1);

    /// <summary>A handler that records sent HTTP requests.</summary>
    /// <remarks>
    /// Multipart content is disposed after sending, so copy it to a byte array during the send.
    /// </remarks>
    private sealed class RequestCapture
    {
        public byte[] Body { get; private set; } = [];

        public string ContentType { get; private set; } = "";

        /// <summary>The body without quotes, used to validate boundary strings and field names.</summary>
        public string Text { get; private set; } = "";

        public bool Record(HttpRequestMessage request)
        {
            if (request.Content is null)
            {
                return false;
            }

            ContentType = request.Content.Headers.ContentType?.ToString() ?? "";
            Body = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            Text = Encoding.Latin1.GetString(Body).Replace("\"", "", StringComparison.Ordinal);

            return true;
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var at = 0; at + needle.Length <= haystack.Length; at++)
        {
            if (haystack.AsSpan(at, needle.Length).SequenceEqual(needle))
            {
                return at;
            }
        }

        return -1;
    }
}
