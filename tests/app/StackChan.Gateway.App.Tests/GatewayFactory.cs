using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.TestKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Starts reference-app endpoints and replaces only external-service communication with test doubles.
/// </summary>
/// <remarks>
/// All components except speech recognition, the agent, and speech synthesis use reference-app
/// implementations. This exercises header validation, SSE composition, sequence numbers, and completion
/// events through one path.
/// </remarks>
internal class GatewayFactory : WebApplicationFactory<Program>
{
    /// <summary>The authentication token. An empty value disables authentication.</summary>
    public string Token { get; init; } = "";

    /// <summary>The number of turns that can be processed concurrently.</summary>
    public int MaxConcurrentTurns { get; init; } = 2;

    /// <summary>
    /// Whether to start in fixed-response mode.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, the normal DI composition uses whisper, piper, and Agent Framework.
    /// This allows service registration and startup validation to be tested without external communication.
    /// </remarks>
    public bool Offline { get; init; } = true;

    /// <summary>
    /// Overrides external-service endpoints. Keys are <c>stt</c>, <c>tts</c>, and <c>model</c>.
    /// </summary>
    /// <remarks>
    /// Keeps health results independent of services running in the test environment.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Endpoints { get; init; } =
        new Dictionary<string, string>();

    public FakeSpeechToText SpeechToText { get; } = new();

    public FakeAgent Agent { get; } = new();

    public FakeTextToSpeech TextToSpeech { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use only TestServer and do not start a real network listener.
        builder.UseSetting("Urls", "");
        builder.UseSetting("StackChan:Atoms3R:Token", Token);
        builder.UseSetting(
            "StackChan:Runtime:MaxConcurrentTurns",
            MaxConcurrentTurns.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // The real-provider composition requires a model token.
        builder.UseSetting("StackChan:Offline:Enabled", Offline ? "true" : "false");

        foreach (var (name, endpoint) in Endpoints)
        {
            builder.UseSetting(
                name switch
                {
                    "stt" => "StackChan:WhisperCpp:Endpoint",
                    "tts" => "StackChan:PiperPlus:Endpoint",
                    "model" => "StackChan:Agent:Endpoint",
                    _ => throw new ArgumentException($"知らない下流の名前: {name}"),
                },
                endpoint);
        }

        if (!Offline)
        {
            return;
        }

        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<ISpeechToText>(SpeechToText));
            services.Replace(ServiceDescriptor.Singleton<IAgent>(Agent));
            services.Replace(ServiceDescriptor.Singleton<ITextToSpeech>(TextToSpeech));
        });
    }
}
