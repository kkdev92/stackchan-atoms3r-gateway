using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kkdev92.StackChan.Gateway.Abstractions.Telemetry;
using Microsoft.Extensions.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Models;

/// <summary>
/// Measures chat model response time and outcome.
/// </summary>
/// <remarks>
/// For streaming responses, this records time to the first update rather than time for the entire
/// response. Total response time varies with output length and is kept separate from initial latency.
/// The turn runtime manages model-call timeouts.
/// </remarks>
/// <param name="innerClient">The chat client to measure.</param>
internal sealed class MeasuredChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    private const string Provider = "model";

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var began = Stopwatch.GetTimestamp();

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);

            GatewayTelemetry.ProviderCalled(Provider, "ok", Stopwatch.GetElapsedTime(began));

            return response;
        }
        catch (OperationCanceledException)
        {
            GatewayTelemetry.ProviderCalled(
                Provider, "cancelled", Stopwatch.GetElapsedTime(began));
            throw;
        }
        catch
        {
            GatewayTelemetry.ProviderCalled(Provider, "failed", Stopwatch.GetElapsedTime(began));
            throw;
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var began = Stopwatch.GetTimestamp();
        var first = true;

        // Drive the async enumerator directly to record outcomes by exception type while yielding.
        var stream = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool moved;

                try
                {
                    moved = await stream.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Note(first, "cancelled", began);
                    throw;
                }
                catch
                {
                    Note(first, "failed", began);
                    throw;
                }

                if (!moved)
                {
                    if (first)
                    {
                        GatewayTelemetry.ProviderCalled(
                            Provider, "empty", Stopwatch.GetElapsedTime(began));
                    }

                    break;
                }

                if (first)
                {
                    first = false;
                    GatewayTelemetry.ProviderCalled(
                        Provider, "ok", Stopwatch.GetElapsedTime(began));
                }

                yield return stream.Current;
            }
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Records an outcome only when the request ends before the first update.
    /// </summary>
    /// <remarks>
    /// A failure after the first update is recorded by the overall turn outcome instead of being
    /// counted again in the connection-start metric.
    /// </remarks>
    private static void Note(bool first, string outcome, long began)
    {
        if (first)
        {
            GatewayTelemetry.ProviderCalled(Provider, outcome, Stopwatch.GetElapsedTime(began));
        }
    }
}
