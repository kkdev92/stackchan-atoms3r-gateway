using System.Collections.Concurrent;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Sessions;
using Kkdev92.StackChan.Gateway.Runtime.Sessions;
using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>
/// Verifies that concurrent session removal and insertion raise no exception.
/// </summary>
/// <remarks>
/// When the count of a <c>ConcurrentDictionary</c> changes during enumeration, the
/// <c>CopyTo</c> that LINQ performs internally can throw <c>ArgumentException</c> or
/// <c>NullReferenceException</c>. Insertion and removal stay safe while idle expiration and
/// the count limit are applied.
/// </remarks>
public sealed class IdleEvictionConcurrencyTests
{
    [Fact]
    public void 件数上限の適用と追加が同時でも_例外を投げない()
    {
        var entries = new ConcurrentDictionary<int, Holder>();
        var failures = new ConcurrentBag<Exception>();
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < 64; index++)
        {
            entries[index] = new Holder(now);
        }

        Parallel.For(0, 4000, index =>
        {
            try
            {
                if (index % 2 == 0)
                {
                    entries[index] = new Holder(now);
                }
                else
                {
                    IdleEviction.Evict(
                        entries, now, TimeSpan.FromMinutes(120), 8, holder => holder.LastUsed);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        });

        failures.ShouldBeEmpty(
            "件数上限の適用中に例外が発生した: " + string.Join(
                " / ", failures.Select(f => f.GetType().Name).Distinct()));
    }

    [Fact]
    public void 期限切れ削除と追加が同時でも_例外を投げない()
    {
        // Expired entries are removed on every pass, even below the count limit.
        var entries = new ConcurrentDictionary<int, Holder>();
        var failures = new ConcurrentBag<Exception>();
        var now = DateTimeOffset.UtcNow;
        var stale = now - TimeSpan.FromHours(5);

        Parallel.For(0, 4000, index =>
        {
            try
            {
                if (index % 2 == 0)
                {
                    entries[index] = new Holder(stale);
                }
                else
                {
                    IdleEviction.Evict(
                        entries, now, TimeSpan.FromMinutes(120), 1000, holder => holder.LastUsed);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        });

        failures.ShouldBeEmpty(
            "期限切れエントリの削除中に例外が発生した: " + string.Join(
                " / ", failures.Select(f => f.GetType().Name).Distinct()));
    }

    [Fact]
    public async Task セッションレジストリへ同時に追加しても_例外を投げない()
    {
        // Reproduce the race between session creation and limit enforcement in the real registry.
        var options = new TurnRuntimeOptions { MaxSessions = 8, SessionIdleTimeoutMinutes = 120 };
        var registry = new InMemorySessionRegistry(TimeProvider.System, options);
        var failures = new ConcurrentBag<Exception>();

        await Task.WhenAll(Enumerable.Range(0, 400).Select(index => Task.Run(async () =>
        {
            try
            {
                await registry.GetOrCreateAsync(
                    new SessionId($"dev-{index}"),
                    new DeviceId($"dev-{index}"),
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        })));

        failures.ShouldBeEmpty(
            "セッションレジストリで例外が発生した: " + string.Join(
                " / ", failures.Select(f => f.GetType().Name).Distinct()));
    }

    private sealed record Holder(DateTimeOffset LastUsed);
}
