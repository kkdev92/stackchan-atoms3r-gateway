namespace Kkdev92.StackChan.Gateway.TestKit;

/// <summary>
/// Creates conversation requests in the same format as an AtomS3R device.
/// </summary>
/// <remarks>
/// Protocol-defined headers are added automatically, so tests do not need to repeat their names.
/// </remarks>
public static class DeviceRequest
{
    /// <summary>The default device ID added to requests.</summary>
    public const string DefaultDevice = "atoms3r-001122334455";

    /// <summary>The default boot ID added to requests.</summary>
    public const string DefaultBoot = "BOOT00000000000000000000AB";

    /// <summary>Creates a conversation request containing WAV audio.</summary>
    /// <param name="wav">The WAV data to send. Defaults to 0.1 seconds of 16 kHz mono silence.</param>
    /// <param name="device">The device ID.</param>
    /// <param name="token">The authentication token. No header is added when this is <see langword="null"/>.</param>
    /// <param name="conversation">The conversation ID.</param>
    /// <param name="baseUrl">The destination base URL. A relative URL is used when omitted.</param>
    public static HttpRequestMessage Speech(
        byte[]? wav = null,
        string device = DefaultDevice,
        string? token = null,
        string conversation = "conv-1",
        string baseUrl = "")
    {
        var request = Create(baseUrl, device, token, conversation);

        request.Content = new ByteArrayContent(wav ?? WavFactory.Wav(new byte[3200], 16000, 1));
        request.Content.Headers.ContentType = new("audio/wav");

        return request;
    }

    /// <summary>Creates a request that starts a conversation from text.</summary>
    /// <param name="text">The text sent as the user's utterance.</param>
    /// <param name="device">The device ID.</param>
    /// <param name="token">The authentication token. No header is added when this is <see langword="null"/>.</param>
    /// <param name="conversation">The conversation ID.</param>
    /// <param name="baseUrl">The destination base URL. A relative URL is used when omitted.</param>
    public static HttpRequestMessage Text(
        string text,
        string device = DefaultDevice,
        string? token = null,
        string conversation = "conv-1",
        string baseUrl = "")
    {
        var request = Create(baseUrl, device, token, conversation);

        request.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { text }),
            System.Text.Encoding.UTF8,
            "application/json");

        return request;
    }

    private static HttpRequestMessage Create(
        string baseUrl,
        string device,
        string? token,
        string conversation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1/converse");

        request.Headers.Add("Accept", "text/event-stream");
        request.Headers.Add("X-StackChan-Device", device);
        request.Headers.Add("X-StackChan-Boot", DefaultBoot);
        request.Headers.Add("X-StackChan-Conversation", conversation);

        if (token is { Length: > 0 })
        {
            request.Headers.Add("X-StackChan-Token", token);
        }

        return request;
    }
}
