using System.Diagnostics;
using System.Net.Sockets;
using Kkdev92.StackChan.Gateway.AgentFramework;
using Kkdev92.StackChan.Gateway.Diagnostics;
using Microsoft.Extensions.Options;
using StackChan.Provider.PiperPlus;
using StackChan.Provider.WhisperCpp;

namespace StackChan.Gateway.App.Health;

/// <summary>Checks connectivity to external providers.</summary>
/// <remarks>
/// The application composes Whisper, Piper, and the language model. The app-specific provider list
/// remains here instead of being embedded in an SDK package.
/// </remarks>
internal static class ProviderHealthEndpoint
{
    // Share recent results to prevent an unauthenticated endpoint from creating a connection burst.
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

    internal readonly record struct Probe(string Name, bool Listening, long Milliseconds);

    // Scope the cache to the DI container so results from different hosts cannot mix.
    internal sealed class ProbeCache
    {
        private readonly Lock _gate = new();

        private (long Stamp, Probe[] Checks)? _last;

        public Probe[]? Fresh()
        {
            lock (_gate)
            {
                return _last is { } last && Stopwatch.GetElapsedTime(last.Stamp) < CacheFor
                    ? last.Checks
                    : null;
            }
        }

        public void Put(Probe[] checks)
        {
            lock (_gate)
            {
                _last = (Stopwatch.GetTimestamp(), checks);
            }
        }
    }

    public static IEndpointRouteBuilder MapProviderHealth(this IEndpointRouteBuilder app)
    {
        // Check only TCP connectivity, without inference, to limit response time and processing load.
        //
        // whisper-server does not respond to GET, so a shared HTTP probe cannot be used.
        // TCP connectivity does not prove full service health, hence the result name "listening".
        // Do not include internal network endpoints in the response.
        app.MapGet("/health/providers", async (
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            var offline = services.GetRequiredService<IOptions<OfflineOptions>>().Value;

            if (offline.Enabled)
            {
                return Results.Json(new { status = "ok", mode = "offline" });
            }

            var whisper = services.GetRequiredService<IOptions<WhisperCppOptions>>().Value;
            var piper = services.GetRequiredService<IOptions<PiperPlusOptions>>().Value;
            var agent = services.GetRequiredService<IOptions<AgentFrameworkOptions>>().Value;

            var checks = await ProbeAsync(
                services.GetRequiredService<ProbeCache>(),
                whisper.Endpoint,
                piper.Endpoint,
                agent.Endpoint,
                cancellationToken);

            var allUp = checks.All(check => check.Listening);

            return Results.Json(
                new
                {
                    status = allUp ? "ok" : "down",
                    providers = checks.ToDictionary(
                        check => check.Name,
                        check => new { listening = check.Listening, ms = check.Milliseconds }),
                },
                statusCode: allUp
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }

    private static async Task<Probe[]> ProbeAsync(
        ProbeCache cache,
        string stt,
        string tts,
        string model,
        CancellationToken cancellationToken)
    {
        if (cache.Fresh() is { } recent)
        {
            return recent;
        }

        var checks = await Task.WhenAll(
            Listening("stt", stt, cancellationToken),
            Listening("tts", tts, cancellationToken),
            Listening("model", model, cancellationToken));

        cache.Put(checks);

        return checks;
    }

    // Return only TCP success and elapsed time, without exposing endpoints or failure details.
    private static async Task<Probe> Listening(
        string name,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var began = Stopwatch.GetTimestamp();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var uri = new Uri(endpoint);

            using var socket = new TcpClient();
            await socket.ConnectAsync(uri.Host, uri.Port, timeout.Token).ConfigureAwait(false);

            return new Probe(name, true, Elapsed(began));
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            return new Probe(name, false, Elapsed(began));
        }

        static long Elapsed(long began) =>
            (long)Stopwatch.GetElapsedTime(began).TotalMilliseconds;
    }
}
