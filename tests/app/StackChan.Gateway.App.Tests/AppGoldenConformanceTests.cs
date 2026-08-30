using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies reference-app endpoint responses against protocol conformance rules.
/// </summary>
/// <remarks>
/// <para>
/// SDK <c>Conformance.Tests</c> covers individual rules and malformed-response mutations. Because
/// <c>SdkGatewayHost</c> does not reference the app, this test additionally covers <c>Program.cs</c>
/// service registration, <c>PostConfigure</c>, and endpoint mapping.
/// </para>
/// <para>
/// Runs the same conversation as the SDK golden test through the app and confirms that composition
/// changes do not introduce conformance violations.
/// </para>
/// </remarks>
public sealed class AppGoldenConformanceTests : IAsyncLifetime
{
    /// <summary>Samples whose Base64 representation contains <c>+</c>.</summary>
    /// <remarks>
    /// The Base64 representation of <c>0xFB 0xF0</c> contains <c>+</c>. These samples are fixed because
    /// the device cannot decode audio if the JSON encoder emits <c>+</c> as a Unicode escape.
    /// </remarks>
    private static readonly short PlusProducingSample = unchecked((short)0xF0FB);

    private const string FirstSentence = "[happy]こんにちは、スタックちゃんです。";

    private const string SecondSentence = "[neutral]きょうは良い天気ですね。";

    private const string Transcript = "こんにちは、元気ですか";

    private GatewayFactory _factory = null!;

    private string? _contentType;

    private byte[] _body = [];

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _factory = new GatewayFactory();
        _factory.SpeechToText.Result = Transcript;

        // Use a sentence longer than the seven-character suffix retained by the streaming formatter.
        _factory.Agent.Fragments = [FirstSentence, SecondSentence];

        var samples = new short[2500];
        samples[0] = PlusProducingSample;
        _factory.TextToSpeech.Result = new PcmAudio(
            samples,
            PcmAudio.CanonicalSampleRate,
            PcmAudio.CanonicalChannels);

        using var client = _factory.CreateClient();
        using var request = DeviceRequest.Speech(conversation: "conv-7");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        _contentType = response.Content.Headers.ContentType?.ToString();
        _body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public void 参照アプリの応答は_すべてのプロトコル適合規則を満たす()
    {
        var violations = ConformanceChecks.Run(
            _contentType,
            _body,
            [Transcript, "こんにちは"]);

        violations.ShouldBeEmpty(
            "違反: " + string.Join(" / ", violations.Select(violation => violation.ToString())));
    }
}
