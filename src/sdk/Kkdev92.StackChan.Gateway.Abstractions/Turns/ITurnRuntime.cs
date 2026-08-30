namespace Kkdev92.StackChan.Gateway.Abstractions.Turns;

/// <summary>
/// Processes one turn and generates events as it progresses.
/// </summary>
/// <remarks>
/// Returned events are transport-independent. The protocol implementation converts them to HTTP
/// and SSE representations.
/// </remarks>
public interface ITurnRuntime
{
    /// <summary>Executes a turn and returns its events in order.</summary>
    /// <param name="request">Turn request received from the device.</param>
    /// <param name="cancellationToken">Token that signals cancellation, such as device disconnection.</param>
    /// <returns>An asynchronous stream of events produced during the turn.</returns>
    IAsyncEnumerable<TurnEvent> ExecuteAsync(
        TurnRequest request,
        CancellationToken cancellationToken);
}
