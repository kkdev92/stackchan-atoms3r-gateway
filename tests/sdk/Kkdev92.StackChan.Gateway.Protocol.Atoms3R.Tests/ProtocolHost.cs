using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Endpoints;
using Kkdev92.StackChan.Gateway.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Tests;

/// <summary>
/// Test host that starts only the Atoms3R conversation endpoint.
/// </summary>
/// <remarks>
/// The turn runtime is replaced with a fake so only the HTTP and SSE translation downstream
/// of the runtime is verified.
/// </remarks>
internal sealed class ProtocolHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private ProtocolHost(WebApplication app, FakeTurnRuntime runtime, HttpClient client)
    {
        _app = app;
        Runtime = runtime;
        Client = client;
    }

    public FakeTurnRuntime Runtime { get; }

    public HttpClient Client { get; }

    public static async Task<ProtocolHost> StartAsync(
        string token = "",
        int keepAliveIntervalSeconds = 3,
        long? maxRequestBodyBytes = null)
    {
        var runtime = new FakeTurnRuntime();

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<ITurnRuntime>(runtime);
        builder.Services.Configure<Atoms3ROptions>(options =>
        {
            options.Token = token;
            options.KeepAliveIntervalSeconds = keepAliveIntervalSeconds;

            if (maxRequestBodyBytes is { } limit)
            {
                options.MaxRequestBodyBytes = limit;
            }
        });

        var app = builder.Build();
        app.MapStackChanAtoms3RConverse();

        await app.StartAsync();

        return new ProtocolHost(app, runtime, app.GetTestClient());
    }

    public async ValueTask DisposeAsync()
    {
        // Release requests waiting on an event before stopping the host.
        Runtime.BlockBeforeFirstEvent?.TrySetResult();

        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
