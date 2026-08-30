namespace Kkdev92.StackChan.Gateway.Abstractions.Sessions;

/// <summary>
/// Manages session creation and last-use times.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>Gets the specified session or creates it when absent.</summary>
    /// <param name="sessionId">Identifier of the session to get or create.</param>
    /// <param name="deviceId">Device identifier associated with the session.</param>
    /// <param name="cancellationToken">Token that signals cancellation.</param>
    /// <returns>A snapshot of the existing or newly created session.</returns>
    ValueTask<SessionSnapshot> GetOrCreateAsync(
        SessionId sessionId,
        DeviceId deviceId,
        CancellationToken cancellationToken);

    /// <summary>Updates the last-use time of a session.</summary>
    /// <param name="sessionId">Identifier of the session to update.</param>
    /// <param name="timestamp">New last-use time.</param>
    /// <param name="cancellationToken">Token that signals cancellation.</param>
    /// <returns>A value that represents the asynchronous operation.</returns>
    ValueTask TouchAsync(
        SessionId sessionId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken);
}
