using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Microsoft.Extensions.Options;

namespace StackChan.Gateway.App.Health;

/// <summary>Registers health endpoints for distinct operational purposes.</summary>
/// <remarks>
/// <para>
/// Process liveness, request readiness, and external-provider connectivity have different costs and
/// consumers, so each is exposed through a separate endpoint.
/// </para>
/// <list type="table">
///   <item><term><c>/health</c></term><description>
///     Liveness. Does not contact external services and can be used to decide whether to restart the process.
///   </description></item>
///   <item><term><c>/health/ready</c></term><description>
///     Readiness. Returns 503 while the app is stopping so callers can stop sending new requests.
///   </description></item>
///   <item><term><c>/health/providers</c></term><description>
///     External-provider connectivity. Intended for troubleshooting; results are cached briefly.
///   </description></item>
/// </list>
/// </remarks>
internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapStackChanHealth(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Liveness requires neither external connections nor configuration details.
        app.MapGet("/health", () => Results.Json(new { status = "ok" }));

        // Readiness returns 503 after shutdown begins so callers know requests are no longer accepted.
        app.MapGet("/health/ready", (
            IHostApplicationLifetime lifetime,
            IOptions<TurnRuntimeOptions> runtime) =>
        {
            var stopping = lifetime.ApplicationStopping.IsCancellationRequested;

            return Results.Json(
                new
                {
                    status = stopping ? "stopping" : "ok",
                    // Expose only the limit, not the active count, so callers can adjust request volume.
                    max_concurrent_turns = runtime.Value.MaxConcurrentTurns,
                },
                statusCode: stopping
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status200OK);
        });

        app.MapProviderHealth();

        return app;
    }
}
