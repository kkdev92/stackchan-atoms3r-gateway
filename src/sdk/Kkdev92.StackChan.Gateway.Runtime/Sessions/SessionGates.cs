using System.Collections.Concurrent;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Sessions;

namespace Kkdev92.StackChan.Gateway.Runtime.Sessions;

/// <summary>Serializes turns that belong to the same session.</summary>
/// <remarks>
/// Each session uses one lock because adding multiple turns to the same conversation concurrently
/// would make the history order nondeterministic.
/// </remarks>
/// <param name="timeProvider">The provider used to obtain last-used timestamps.</param>
/// <param name="maxSessions">The maximum number of locks to retain.</param>
/// <param name="idleTimeout">The time before an unused lock is discarded.</param>
internal sealed class SessionGates(
    TimeProvider timeProvider,
    int maxSessions,
    TimeSpan idleTimeout) : IDisposable
{
    private readonly ConcurrentDictionary<SessionId, Entry> _gates = new();

    /// <summary>Gets the number of session locks currently retained.</summary>
    public int Count => _gates.Count;

    /// <summary>Acquires the lock for a session.</summary>
    /// <remarks>
    /// If the lock is removed while the caller is waiting, this method acquires the replacement
    /// lock. The recheck prevents old and new locks from being used for the same session at once.
    /// </remarks>
    /// <param name="sessionId">The target session.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>The acquired lock. Call <c>Gate.Release()</c> after use.</returns>
    public async Task<Entry> AcquireAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = _gates.GetOrAdd(sessionId, _ => new Entry(timeProvider));

            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (_gates.TryGetValue(sessionId, out var current) &&
                ReferenceEquals(current, entry))
            {
                entry.Touch(timeProvider);

                return entry;
            }

            // Do not use a lock removed during the wait; acquire the currently registered lock.
            entry.Gate.Release();
        }
    }

    /// <summary>Removes unused session locks.</summary>
    /// <remarks>
    /// Locks that are held or have waiters are not removed. Locks past the idle timeout are removed
    /// first. If the count exceeds the limit, unused locks are then removed from least recently used.
    /// </remarks>
    /// <param name="now">The timestamp used to determine which locks to remove.</param>
    public void EvictIdle(DateTimeOffset now) =>
        // Exclude locks in use because removing one could create two locks for the same session.
        IdleEviction.Evict(
            _gates,
            now,
            idleTimeout,
            maxSessions,
            static gate => gate.LastUsed,
            static gate => gate.Gate.CurrentCount == 1);

    /// <summary>Disposes the retained session locks.</summary>
    public void Dispose()
    {
        foreach (var pair in _gates)
        {
            if (_gates.TryRemove(pair))
            {
                pair.Value.Gate.Dispose();
            }
        }
    }

    /// <summary>
    /// Holds the lock and last-used timestamp for one session.
    /// </summary>
    /// <remarks>
    /// Keeping the lock and timestamp in one entry allows eviction candidates to be identified
    /// without locking the entire dictionary.
    /// </remarks>
    internal sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        private long _lastUsedTicks;

        public Entry(TimeProvider timeProvider) => Touch(timeProvider);

        public void Touch(TimeProvider timeProvider) =>
            Interlocked.Exchange(ref _lastUsedTicks, timeProvider.GetUtcNow().UtcTicks);

        public DateTimeOffset LastUsed =>
            new(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);

        public bool IdleLongerThan(TimeSpan ttl, DateTimeOffset now) => now - LastUsed >= ttl;
    }
}
