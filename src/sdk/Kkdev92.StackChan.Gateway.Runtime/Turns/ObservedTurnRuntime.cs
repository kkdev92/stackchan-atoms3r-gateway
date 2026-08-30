using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kkdev92.StackChan.Gateway.Abstractions.Telemetry;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;

namespace Kkdev92.StackChan.Gateway.Runtime.Turns;

/// <summary>
/// A runtime decorator that records turn metrics and traces.
/// </summary>
/// <remarks>
/// Because the decorator observes only the events returned by a turn, it measures all completion
/// paths consistently, including rejected requests and downstream failures. It records event types,
/// elapsed time, and time to first audio, but not user utterances or response text.
/// </remarks>
/// <param name="inner">The runtime that performs the turn.</param>
/// <param name="timeProvider">The time provider used for measurements.</param>
public sealed class ObservedTurnRuntime(ITurnRuntime inner, TimeProvider timeProvider) : ITurnRuntime
{
    /// <inheritdoc />
    public async IAsyncEnumerable<TurnEvent> ExecuteAsync(
        TurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Device IDs have high cardinality and are therefore excluded from metric attributes.
        using var activity = GatewayTelemetry.Source.StartActivity("stackchan.turn");

        activity?.SetTag("stackchan.input", request.UserText is null ? "audio" : "text");

        var began = timeProvider.GetTimestamp();
        var spoke = false;

        GatewayTelemetry.ActiveTurnsChanged(1);

        try
        {
            await foreach (var turnEvent in inner
                .ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false))
            {
                switch (turnEvent)
                {
                    case ReplyAudioAvailable when !spoke:
                        spoke = true;
                        GatewayTelemetry.FirstAudioSent(
                            timeProvider.GetElapsedTime(began));
                        activity?.SetTag("stackchan.spoke", true);
                        break;

                    case TurnCompleted completed:
                        GatewayTelemetry.TurnEnded(
                            Outcome(completed.Reason),
                            timeProvider.GetElapsedTime(began));
                        activity?.SetTag("stackchan.outcome", Outcome(completed.Reason));
                        activity?.SetStatus(
                            completed.Reason == TurnCompletionReason.Completed
                                ? ActivityStatusCode.Ok
                                : ActivityStatusCode.Error);
                        break;

                    default:
                        break;
                }

                yield return turnEvent;
            }
        }
        finally
        {
            // Decrement the active count even if the caller stops enumerating events early.
            GatewayTelemetry.ActiveTurnsChanged(-1);
        }
    }

    /// <summary>
    /// Converts a completion reason to a low-cardinality metric attribute.
    /// </summary>
    private static string Outcome(TurnCompletionReason reason) => reason switch
    {
        TurnCompletionReason.Completed => "completed",
        TurnCompletionReason.Failed => "failed",
        TurnCompletionReason.Cancelled => "cancelled",
        _ => "other",
    };
}
