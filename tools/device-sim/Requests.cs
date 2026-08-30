using System.Globalization;
using System.Text;
using Kkdev92.StackChan.Gateway.TestKit;

namespace StackChan.DeviceSim;

/// <summary>Creates normal conversation requests and malformed requests for validation.</summary>
/// <remarks>
/// Normal requests use <see cref="DeviceRequest"/> to preserve the same protocol format as conformance tests.
/// </remarks>
internal static class Requests
{
    /// <summary>The default utterance used by validation scenarios.</summary>
    /// <remarks>
    /// The default WAV is silent and cannot be transcribed, so scenarios that validate conversation
    /// completion send this text instead.
    /// </remarks>
    public const string DefaultUtterance = "こんにちは";

    public static HttpRequestMessage Normal(
        Settings settings,
        string conversation,
        string? device = null,
        string? text = null)
    {
        var name = device ?? settings.Device;

        if ((text ?? settings.Text) is { } spoken)
        {
            return DeviceRequest.Text(
                spoken, name, settings.Token, conversation, settings.Gateway);
        }

        // Send 16 kHz, 16-bit mono silence for validations that do not need recognition results.
        var wav = WavFactory.Wav(new byte[16000 * 2 * settings.Seconds], 16000, 1);

        return DeviceRequest.Speech(
            wav, name, settings.Token, conversation, settings.Gateway);
    }

    public static HttpRequestMessage Text(
        Settings settings,
        string text,
        string conversation) =>
        DeviceRequest.Text(
            text, settings.Device, settings.Token, conversation, settings.Gateway);

    /// <summary>Returns requests rejected by input validation and their expected responses.</summary>
    /// <remarks>
    /// Each case includes the expected HTTP status code and error message.
    /// </remarks>
    public static IReadOnlyList<(string Name, int Status, string Reason, HttpRequestMessage Request)>
        Rejected(Settings settings) =>
    [
        ("missing device ID", 400, "device header is required",
            Raw(settings, device: "", body: Json("こんにちは"))),

        ("device ID is too long", 400, "device header is malformed",
            Raw(settings, device: new string('d', 200), body: Json("こんにちは"))),

        ("device ID contains an invalid character", 400, "device header is malformed",
            Raw(settings, device: "atoms3r 001", body: Json("こんにちは"))),

        ("boot ID is too long", 400, "boot header is malformed",
            Raw(settings, boot: new string('b', 200), body: Json("こんにちは"))),

        ("utterance text is missing", 400, "text is required",
            Raw(settings, body: new StringContent(
                "{}", Encoding.UTF8, "application/json"))),

        ("malformed JSON", 400, "text is required",
            Raw(settings, body: new StringContent(
                "{\"text\":", Encoding.UTF8, "application/json"))),

        ("utterance text is too long", 413, "text is too large",
            Raw(settings, body: Json(new string('あ', 2000)))),

        ("body is not WAV data", 400, "wav is required",
            Raw(settings, body: new ByteArrayContent(Encoding.UTF8.GetBytes("not a WAV file"))
            {
                Headers = { ContentType = new("audio/wav") },
            })),
    ];

    private static StringContent Json(string text) =>
        new(
            System.Text.Json.JsonSerializer.Serialize(new { text }),
            Encoding.UTF8,
            "application/json");

    // Send malformed headers that cannot be created through DeviceRequest to input validation.
    private static HttpRequestMessage Raw(
        Settings settings,
        HttpContent body,
        string? device = null,
        string? boot = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, settings.Gateway + "/v1/converse")
        {
            Content = body,
        };

        request.Headers.Add("Accept", "text/event-stream");
        request.Headers.TryAddWithoutValidation(
            "X-StackChan-Device", device ?? settings.Device);
        request.Headers.TryAddWithoutValidation(
            "X-StackChan-Boot", boot ?? DeviceRequest.DefaultBoot);
        request.Headers.TryAddWithoutValidation(
            "X-StackChan-Conversation",
            "reject-" + Environment.TickCount64.ToString(CultureInfo.InvariantCulture));

        if (settings.Token is { Length: > 0 } token)
        {
            request.Headers.Add("X-StackChan-Token", token);
        }

        return request;
    }
}
