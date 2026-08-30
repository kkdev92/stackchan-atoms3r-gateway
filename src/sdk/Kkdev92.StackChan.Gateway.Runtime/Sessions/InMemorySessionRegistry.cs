using System.Collections.Concurrent;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Sessions;
using Kkdev92.StackChan.Gateway.Runtime.Turns;

namespace Kkdev92.StackChan.Gateway.Runtime.Sessions;

/// <summary>
/// Stores sessions in process memory.
/// </summary>
/// <remarks>
/// Sessions are not persisted, so the next turn after a process restart begins a new conversation.
/// Limits on session count and idle time prevent memory usage from growing indefinitely in a
/// long-running process. The least recently used idle sessions are removed first.
/// </remarks>
/// <param name="timeProvider">The time provider used to track session activity.</param>
/// <param name="options">The session count and idle-time settings.</param>
public sealed class InMemorySessionRegistry(
    TimeProvider timeProvider,
    TurnRuntimeOptions options) : ISessionRegistry
{
    private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

    /// <summary>Gets the number of sessions currently retained.</summary>
    public int Count => _sessions.Count;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The specified session is associated with a different device.
    /// </exception>
    public ValueTask<SessionSnapshot> GetOrCreateAsync(
        SessionId sessionId,
        DeviceId deviceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();

        // Evict on access so that the idle timeout is enforced even below the session-count limit.
        Evict(now);

        var session = _sessions.GetOrAdd(
            sessionId,
            static (id, state) => new SessionSnapshot(id, state.DeviceId, state.Now, state.Now),
            (DeviceId: deviceId, Now: now));

        if (session.DeviceId != deviceId)
        {
            throw new InvalidOperationException(
                "The session is already bound to a different device.");
        }

        return ValueTask.FromResult(session);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The entry is replaced conditionally so that an update made after the read is not overwritten.
    /// </remarks>
    public ValueTask TouchAsync(
        SessionId sessionId,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _sessions.TryUpdate(sessionId, session with { LastActivityAt = timestamp }, session);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Removes expired sessions and sessions over the configured limit.
    /// </summary>
    /// <remarks>
    /// Sessions past the idle timeout are removed first. If the count still exceeds the limit,
    /// the least recently used sessions are removed next.
    /// </remarks>
    private void Evict(DateTimeOffset now) =>
        IdleEviction.Evict(
            _sessions,
            now,
            TimeSpan.FromMinutes(options.SessionIdleTimeoutMinutes),
            options.MaxSessions,
            static session => session.LastActivityAt);
}
