using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Endpoints;
using Kkdev92.StackChan.Gateway.Runtime;
using Kkdev92.StackChan.Gateway.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kkdev92.StackChan.Gateway.Conformance.Tests;

/// <summary>
/// A minimal gateway composed only of SDK packages.
/// </summary>
/// <remarks>
/// <para>
/// Conformance tests use this host to verify that <c>/v1/converse</c> can be composed solely from
/// public SDK APIs without referencing the reference application.
/// </para>
/// <para>
/// SDK implementations provide the turn runtime and protocol. Only external speech recognition,
/// agent, and speech synthesis dependencies are replaced with TestKit fakes.
/// </para>
/// </remarks>
internal sealed class SdkGatewayHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private SdkGatewayHost(
        WebApplication app,
        FakeSpeechToText speechToText,
        FakeAgent agent,
        FakeTextToSpeech textToSpeech,
        HttpClient client)
    {
        _app = app;
        SpeechToText = speechToText;
        Agent = agent;
        TextToSpeech = textToSpeech;
        Client = client;
    }

    /// <summary>The speech recognition implementation used by tests.</summary>
    public FakeSpeechToText SpeechToText { get; }

    /// <summary>The agent implementation used by tests.</summary>
    public FakeAgent Agent { get; }

    /// <summary>The speech synthesis implementation used by tests.</summary>
    public FakeTextToSpeech TextToSpeech { get; }

    /// <summary>The client used to send requests to the test host.</summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Starts the test host.
    /// </summary>
    /// <remarks>
    /// Supplies in-memory configuration loaded through each SDK package's service-registration extension.
    /// </remarks>
    /// <param name="configure">A function that overrides default settings.</param>
    public static async Task<SdkGatewayHost> StartAsync(
        Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["StackChan:Runtime:MaxConcurrentTurns"] = "2",
            ["StackChan:Runtime:TurnTimeoutSeconds"] = "120",
            ["StackChan:Atoms3R:Token"] = "",
            ["StackChan:Atoms3R:MaxRequestBodyBytes"] = "2097152",
            ["StackChan:Atoms3R:KeepAliveIntervalSeconds"] = "3",
        };

        configure?.Invoke(settings);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings);

        // Compose the host only through public SDK service-registration extensions.
        builder.Services.AddStackChanRuntime(builder.Configuration);
        builder.Services.AddStackChanAtoms3R(builder.Configuration);

        // Fake only external services; use SDK implementations for request validation and SSE generation.
        var speechToText = new FakeSpeechToText();
        var agent = new FakeAgent();
        var textToSpeech = new FakeTextToSpeech();

        builder.Services.Replace(ServiceDescriptor.Singleton<ISpeechToText>(speechToText));
        builder.Services.Replace(ServiceDescriptor.Singleton<IAgent>(agent));
        builder.Services.Replace(ServiceDescriptor.Singleton<ITextToSpeech>(textToSpeech));

        var app = builder.Build();
        app.MapStackChanAtoms3RConverse();

        await app.StartAsync();

        return new SdkGatewayHost(app, speechToText, agent, textToSpeech, app.GetTestClient());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
