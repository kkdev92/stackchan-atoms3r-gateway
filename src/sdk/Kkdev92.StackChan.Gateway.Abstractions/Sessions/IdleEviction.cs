using System.Collections.Concurrent;

namespace Kkdev92.StackChan.Gateway.Abstractions.Sessions;

/// <summary>Removes unused entries from a concurrently accessed collection.</summary>
public static class IdleEviction
{
    /// <summary>Removes expired entries, then removes the oldest entries above the limit.</summary>
    /// <typeparam name="TKey">Collection key type.</typeparam>
    /// <typeparam name="TValue">Collection value type.</typeparam>
    /// <param name="entries">Collection from which entries are removed.</param>
    /// <param name="now">Reference time used to calculate idle duration.</param>
    /// <param name="idleTimeout">Duration after which an entry is considered idle.</param>
    /// <param name="maxEntries">Maximum entries retained after expiration.</param>
    /// <param name="lastUsed">Function that obtains the last-use time from a value.</param>
    /// <param name="removable">
    /// Function that returns <see langword="true"/> for removable values. When
    /// <see langword="null"/>, every value can be removed.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entries"/> or <paramref name="lastUsed"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// A value replaced after candidate selection is not removed. The collection may remain above
    /// <paramref name="maxEntries"/> when <paramref name="removable"/> protects entries or when
    /// concurrent additions and updates continue during eviction.
    /// </remarks>
    public static void Evict<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> entries,
        DateTimeOffset now,
        TimeSpan idleTimeout,
        int maxEntries,
        Func<TValue, DateTimeOffset> lastUsed,
        Func<TValue, bool>? removable = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(lastUsed);

        foreach (var pair in entries)
        {
            if (now - lastUsed(pair.Value) >= idleTimeout &&
                (removable is null || removable(pair.Value)))
            {
                entries.TryRemove(pair);
            }
        }

        var excess = entries.Count - maxEntries;

        if (excess <= 0)
        {
            return;
        }

        // LINQ may use ICollection.CopyTo, which is unsafe when the dictionary count changes during
        // enumeration. Copy candidates through the public enumerator before sorting them.
        var candidates = new List<KeyValuePair<TKey, TValue>>();

        foreach (var pair in entries)
        {
            if (removable is null || removable(pair.Value))
            {
                candidates.Add(pair);
            }
        }

        candidates.Sort(
            (left, right) => lastUsed(left.Value).CompareTo(lastUsed(right.Value)));

        for (var index = 0; index < excess && index < candidates.Count; index++)
        {
            entries.TryRemove(candidates[index]);
        }
    }
}
