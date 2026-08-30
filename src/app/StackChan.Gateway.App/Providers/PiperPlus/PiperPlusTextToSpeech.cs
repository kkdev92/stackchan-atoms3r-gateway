using System.Globalization;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Providers.Audio;
using Kkdev92.StackChan.Gateway.Providers.Http;

namespace StackChan.Provider.PiperPlus;

/// <summary>Configures speech synthesis with piper-plus.</summary>
public sealed class PiperPlusOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "StackChan:PiperPlus";

    /// <summary>The piper-plus server base URL. The default port is 5000.</summary>
    public string Endpoint { get; set; } = "http://127.0.0.1:5000";

    /// <summary>The speech synthesis API path.</summary>
    /// <remarks>
    /// <c>/tts_live.wav</c> streams audio during synthesis; <c>/tts_stream.wav</c> returns audio after synthesis completes.
    /// </remarks>
    public string Path { get; set; } = "/tts_live.wav";

    /// <summary>The speech-duration multiplier. 0.5 is faster, 1.0 is normal, and 2.0 is slower.</summary>
    public double LengthScale { get; set; } = 1.0;

    /// <summary>The voice name. An empty value uses the server default.</summary>
    public string Character { get; set; } = "";

    /// <summary>The number of seconds to wait for one sentence to be synthesized.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>The maximum WAV response size accepted from the speech synthesis API, in bytes.</summary>
    /// <remarks>
    /// The limit is applied before loading the response into memory to prevent excessive allocation.
    /// </remarks>
    public int MaxResponseBytes { get; set; } = 8 * 1024 * 1024;
}

/// <summary>Converts text to speech with piper-plus.</summary>
/// <remarks>
/// <c>GET {endpoint}/tts_live.wav?text=…&amp;length_scale=…&amp;character=…</c> returns WAV data.
/// Because the sample rate depends on the voice model, output is converted to the gateway's canonical
/// 16 kHz mono PCM format. This provider does not perform protocol-specific chunking or encoding.
/// </remarks>
/// <param name="httpClient">
/// A function that obtains an <see cref="HttpClient"/> for each request.
/// This is normally <c>IHttpClientFactory.CreateClient</c>.
/// </param>
/// <param name="options">The endpoint and voice settings.</param>
/// <param name="breaker">A shared circuit breaker. A new instance is created when omitted.</param>
public sealed class PiperPlusTextToSpeech(
    Func<HttpClient> httpClient,
    PiperPlusOptions options,
    ProviderCircuitBreaker? breaker = null)
    : ITextToSpeech
{
    /// <summary>The name of the named <see cref="HttpClient"/> for piper-plus.</summary>
    public const string HttpClientName = "piper";

    // While the circuit is open, the runtime sends text in place of audio.
    private readonly ProviderCircuitBreaker _breaker = breaker ?? new ProviderCircuitBreaker("tts");

    /// <inheritdoc />
    /// <exception cref="ProviderException">Speech could not be synthesized.</exception>
    public async Task<PcmAudio> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        // Omit an empty character parameter because the server treats it as an unknown voice, not as unspecified.
        var url = options.Endpoint.TrimEnd('/') + options.Path +
                  "?text=" + Uri.EscapeDataString(text) +
                  "&length_scale=" + options.LengthScale.ToString("0.00", CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(options.Character))
        {
            url += "&character=" + Uri.EscapeDataString(options.Character);
        }

        // Stop on whichever occurs first: device disconnection or provider timeout.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        return await _breaker.RunAsync(
            _ => SynthesizeOnceAsync(url, timeout.Token, cancellationToken),
            "speech synthesis is unavailable",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PcmAudio> SynthesizeOnceAsync(
        string url,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient()
                .GetAsync(url, deadline)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;

                throw ProviderEndpoint.Unavailable(
                    "speech synthesis failed",
                    new InvalidOperationException($"piper answered {status}"),
                    ProviderEndpoint.IsRetryableStatus(status));
            }

            // Read no more than the configured limit, whether or not Content-Length is present.
            var wav = await ProviderResponse
                .ReadAtMostAsync(
                    response.Content,
                    options.MaxResponseBytes,
                    "speech synthesis failed",
                    deadline)
                .ConfigureAwait(false);

            return new PcmAudio(
                WavAudio.ToTargetPcm(wav),
                PcmAudio.CanonicalSampleRate,
                PcmAudio.CanonicalChannels);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderEndpoint.Timeout("speech synthesis timed out");
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable("piper is unreachable", exception);
        }
        catch (InvalidDataException exception)
        {
            // A malformed WAV will not improve when the same request is retried.
            throw ProviderEndpoint.Unavailable(
                "speech synthesis failed", exception, retryable: false);
        }
    }

    // Hide the endpoint and normalize exceptions so the runtime can handle them per sentence.
    private static ProviderException Unavailable(string detail, Exception? inner = null) =>
        ProviderEndpoint.Unavailable(
            "speech synthesis failed",
            inner ?? new InvalidOperationException(detail));
}
