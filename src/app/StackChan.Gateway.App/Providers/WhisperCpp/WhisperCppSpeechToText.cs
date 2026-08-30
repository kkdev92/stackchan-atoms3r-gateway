using System.Text.Json;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Providers.Audio;
using Kkdev92.StackChan.Gateway.Providers.Http;
using Kkdev92.StackChan.Gateway.Providers.Text;

namespace StackChan.Provider.WhisperCpp;

/// <summary>Converts speech to text with whisper.cpp.</summary>
/// <remarks>
/// A multipart/form-data request to <c>POST {endpoint}/inference</c> sends the file, temperature,
/// temperature_inc, response_format, and language fields and receives <c>{"text":"..."}</c>.
/// Audio and transcripts are not logged. <see cref="NonSpeechAnnotations"/> removes non-speech
/// annotations from recognition results.
/// </remarks>
/// <param name="httpClient">
/// A function that obtains an <see cref="HttpClient"/> for each request.
/// This is normally <c>IHttpClientFactory.CreateClient</c>.
/// </param>
/// <param name="options">The endpoint and recognition settings.</param>
/// <param name="breaker">A shared circuit breaker. A new instance is created when omitted.</param>
public sealed class WhisperCppSpeechToText(
    Func<HttpClient> httpClient,
    WhisperCppOptions options,
    ProviderCircuitBreaker? breaker = null)
    : ISpeechToText
{
    /// <summary>The name of the named <see cref="HttpClient"/> for whisper.cpp.</summary>
    public const string HttpClientName = "whisper";

    private readonly ProviderCircuitBreaker _breaker = breaker ?? new ProviderCircuitBreaker("stt");

    /// <inheritdoc />
    /// <exception cref="ProviderException">Speech could not be recognized.</exception>
    public async Task<Transcript> TranscribeAsync(
        PcmAudio audio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var url = options.Endpoint.TrimEnd('/') + options.Path;

        // Wrap the input PCM in WAV because the whisper.cpp API accepts a file.
        var wav = WavWriter.Build(audio);

        // Request detailed verbose_json only when language probability is needed.
        var checksLanguage = ChecksLanguage(options);

        using var form = new MultipartFormDataContent
        {
            { new StringContent("0.0"), "temperature" },
            { new StringContent("0.2"), "temperature_inc" },
            { new StringContent(checksLanguage ? "verbose_json" : "json"), "response_format" },
        };

        if (!string.IsNullOrEmpty(options.Language))
        {
            form.Add(new StringContent(options.Language), "language");
        }

        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new("application/octet-stream");
        form.Add(file, "file", "speak.wav");

        // Stop on whichever occurs first: device disconnection or provider timeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        return await _breaker.RunAsync(
            _ => TranscribeOnceAsync(url, form, checksLanguage, timeout.Token, cancellationToken),
            "speech recognition is unavailable",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Transcript> TranscribeOnceAsync(
        string url,
        MultipartFormDataContent form,
        bool checksLanguage,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        var timeout = deadline;

        try
        {
            using var response = await httpClient()
                .PostAsync(url, form, timeout)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;

                throw ProviderEndpoint.Unavailable(
                    "speech recognition failed",
                    new InvalidOperationException($"whisper answered {status}"),
                    ProviderEndpoint.IsRetryableStatus(status));
            }

            // Limit response size before parsing JSON to bound memory use from a nonterminating stream.
            var body = await ProviderResponse
                .ReadAtMostAsync(
                    response.Content,
                    options.MaxResponseBytes,
                    "speech recognition failed",
                    timeout)
                .ConfigureAwait(false);

            using var json = JsonDocument.Parse(body);

            var text = json.RootElement.TryGetProperty("text", out var value)
                ? value.GetString()
                : null;

            // Treat audio below the language-probability threshold as an empty utterance.
            if (checksLanguage && !SoundsLikeLanguage(json.RootElement, options))
            {
                return new Transcript("");
            }

            // A result containing only non-speech annotations becomes empty and is handled as no speech.
            return new Transcript(NonSpeechAnnotations.Strip(text));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderEndpoint.Timeout("speech recognition timed out");
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable("whisper is unreachable", exception);
        }
        catch (JsonException exception)
        {
            throw Unavailable("whisper returned a malformed answer", exception);
        }
    }

    private static bool ChecksLanguage(WhisperCppOptions options) =>
        options.MinLanguageProbability > 0;

    private static bool DetectsLanguage(WhisperCppOptions options) =>
        string.IsNullOrEmpty(options.Language) ||
        string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns whether the recognition result meets the language-probability threshold.</summary>
    /// <remarks>
    /// This uses <c>language_probabilities</c> when a language is specified and
    /// <c>detected_language_probability</c> for automatic detection. Compatible implementations
    /// that do not return a probability are accepted without discarding the recognition result.
    /// </remarks>
    private static bool SoundsLikeLanguage(JsonElement root, WhisperCppOptions options)
    {
        var probability = DetectsLanguage(options)
            ? Number(root, "detected_language_probability")
            : LanguageProbability(root, options.Language);

        // Accept whisper.cpp-compatible implementations that do not return a probability.
        return probability is null || probability >= options.MinLanguageProbability;
    }

    private static double? LanguageProbability(JsonElement root, string language) =>
        root.TryGetProperty("language_probabilities", out var probabilities) &&
        probabilities.ValueKind == JsonValueKind.Object
            ? Number(probabilities, language)
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static ProviderException Unavailable(string detail, Exception? inner = null) =>
        ProviderEndpoint.Unavailable(
            "speech recognition failed",
            inner ?? new InvalidOperationException(detail));
}
