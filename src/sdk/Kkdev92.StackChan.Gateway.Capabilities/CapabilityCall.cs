using Kkdev92.StackChan.Gateway.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace Kkdev92.StackChan.Gateway.Capabilities;

/// <summary>Applies a capability deadline and replaces failures with a speakable message.</summary>
/// <remarks>
/// <para>
/// Runtime failures, including timeouts, are converted to a predefined message so a capability
/// failure does not interrupt the entire turn.
/// </para>
/// <para>
/// Only cancellation requested by the caller propagates as <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// This helper cannot forcibly terminate running work. The delegate must observe the supplied
/// <see cref="CancellationToken"/> and respond to cancellation.
/// </para>
/// <para>
/// Exceptions are passed unchanged to the supplied <see cref="ILogger"/>. Exception messages must
/// not contain endpoints, credentials, or other sensitive information.
/// </para>
/// </remarks>
public static class CapabilityCall
{
    /// <summary>Runs work with a cancellation token that includes a deadline.</summary>
    /// <param name="work">Work to run. It receives a token combining the deadline and caller cancellation.</param>
    /// <param name="whenUnavailable">Non-empty message returned after a failure or timeout.</param>
    /// <param name="timeout">Delay before cancellation is signaled to <paramref name="work"/>.</param>
    /// <param name="cancellationToken">Token that signals cancellation by the caller.</param>
    /// <param name="logger">Failure destination, or <see langword="null"/> to disable logging.</param>
    /// <param name="name">Capability name recorded in logs and metrics; defaults to <c>(unnamed)</c>.</param>
    /// <returns>The work result, or <paramref name="whenUnavailable"/> after a failure or timeout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="whenUnavailable"/> is <see langword="null"/> or empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is outside the supported range.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> signaled cancellation. A timeout returns
    /// <paramref name="whenUnavailable"/> instead of throwing.
    /// </exception>
    public static async Task<string> AnswerAsync(
        Func<CancellationToken, Task<string>> work,
        string whenUnavailable,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentException.ThrowIfNullOrEmpty(whenUnavailable);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            var result = await work(deadline.Token).ConfigureAwait(false);

            GatewayTelemetry.CapabilityCalled(name ?? "(unnamed)", "ok");

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Propagate caller cancellation separately from a timeout.
            throw;
        }
        catch (OperationCanceledException)
        {
            Note(logger, name, "timeout", exception: null);

            return whenUnavailable;
        }
        catch (Exception exception)
        {
            Note(logger, name, "failed", exception);

            return whenUnavailable;
        }
    }

    // A diagnostic failure must not prevent the fallback message from being returned.
    private static void Note(ILogger? logger, string? name, string stage, Exception? exception)
    {
        GatewayTelemetry.CapabilityCalled(name ?? "(unnamed)", stage);

        try
        {
            logger?.LogWarning(
                exception, "capability name={Name} stage={Stage}", name ?? "(unnamed)", stage);
        }
        catch (Exception thrown) when (thrown is not OperationCanceledException)
        {
        }
    }
}
