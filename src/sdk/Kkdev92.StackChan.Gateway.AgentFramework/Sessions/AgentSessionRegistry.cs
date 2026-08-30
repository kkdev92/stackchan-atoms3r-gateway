using System.Collections.Concurrent;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Sessions;
using Microsoft.Agents.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Sessions;

/// <summary>
/// Associates StackChan session IDs with Agent Framework conversation state.
/// </summary>
/// <remarks>
/// Conversation state is retained only in the current process. The next turn after a restart or
/// session eviction starts a new conversation. Count and idle-time limits prevent conversation
/// history from growing indefinitely in memory. Eviction does not affect a turn already in progress.
/// </remarks>
/// <param name="create">A function that creates Agent Framework conversation state.</param>
/// <param name="options">The session count and idle-time settings.</param>
/// <param name="timeProvider">The provider used to obtain last-used timestamps.</param>
internal sealed class AgentSessionRegistry(
    Func<CancellationToken, ValueTask<AgentSession>> create,
    AgentFrameworkOptions options,
    TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<SessionId, Entry> _sessions = new();

    public int Count => _sessions.Count;

    /// <summary>Returns existing conversation state or creates it when absent.</summary>
    /// <remarks>
    /// A failed creation is not retained, allowing the next turn to retry.
    /// </remarks>
    public async Task<AgentSession> GetOrCreateAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Evict on access so that the idle timeout is enforced even below the session-count limit.
        Evict(now);

        var entry = _sessions.GetOrAdd(
            sessionId,
            _ => new Entry(new Lazy<Task<AgentSession>>(
                () => create(cancellationToken).AsTask(),
                LazyThreadSafetyMode.ExecutionAndPublication)));

        entry.Touch(now);

        try
        {
            return await entry.Session.Value.ConfigureAwait(false);
        }
        catch
        {
            _sessions.TryRemove(new KeyValuePair<SessionId, Entry>(sessionId, entry));
            throw;
        }
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
            static entry => entry.LastUsed);

    private sealed class Entry(Lazy<Task<AgentSession>> session)
    {
        private long _lastUsedTicks;

        public Lazy<Task<AgentSession>> Session { get; } = session;

        public DateTimeOffset LastUsed =>
            new(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);

        public void Touch(DateTimeOffset now) =>
            Interlocked.Exchange(ref _lastUsedTicks, now.UtcTicks);
    }
}
