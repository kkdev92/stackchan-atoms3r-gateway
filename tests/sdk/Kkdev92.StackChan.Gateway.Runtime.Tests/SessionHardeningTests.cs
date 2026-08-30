using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Runtime.Sessions;
using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>Verifies session-registry count limits and idle expiration.</summary>
/// <remarks>
/// Old sessions are evicted so requests with arbitrary device IDs cannot grow the registry indefinitely.
/// Small limits verify that an evicted session restarts safely with new history.
/// </remarks>
public sealed class SessionHardeningTests
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static SessionId Session(string name) => new(name);

    private static DeviceId Device(string name) => new(name);

    /// <summary>Evicts a session past the idle timeout even below the count limit.</summary>
    /// <remarks>
    /// <c>SessionIdleTimeoutMinutes</c> applies regardless of registry count.
    /// </remarks>
    [Fact]
    public async Task 件数上限に余裕があっても_アイドル期限を超えたセッションを破棄する()
    {
        var clock = new TestTimeProvider(Origin);
        var registry = new InMemorySessionRegistry(
            clock,
            new TurnRuntimeOptions { MaxSessions = 128, SessionIdleTimeoutMinutes = 60 });

        await registry.GetOrCreateAsync(
            Session("a"), Device("a"), TestContext.Current.CancellationToken);

        registry.Count.ShouldBe(1);

        // Add another session after three hours to trigger expiration.
        clock.Advance(TimeSpan.FromHours(3));

        await registry.GetOrCreateAsync(
            Session("b"), Device("b"), TestContext.Current.CancellationToken);

        // Evict expired session a and retain only b.
        registry.Count.ShouldBe(1);

        // The next request for a starts a new session without history.
        var restarted = await registry.GetOrCreateAsync(
            Session("a"), Device("z"), TestContext.Current.CancellationToken);

        restarted.DeviceId.Value.ShouldBe("z");
    }

    [Fact]
    public async Task アイドル期限を超えたセッションは_次の要求で新しく作成する()
    {
        var clock = new TestTimeProvider(Origin);
        var registry = new InMemorySessionRegistry(
            clock,
            new TurnRuntimeOptions { MaxSessions = 1, SessionIdleTimeoutMinutes = 1 });

        var first = await registry.GetOrCreateAsync(
            Session("a"), Device("a"), TestContext.Current.CancellationToken);

        // Add b after a expires, removing a from the registry.
        clock.Advance(TimeSpan.FromMinutes(2));
        await registry.GetOrCreateAsync(
            Session("b"), Device("b"), TestContext.Current.CancellationToken);

        registry.Count.ShouldBe(1);

        // Retain recently used session b.
        clock.Advance(TimeSpan.FromSeconds(10));
        await registry.GetOrCreateAsync(
            Session("b"), Device("b"), TestContext.Current.CancellationToken);

        registry.Count.ShouldBe(1);

        // Restart evicted session a with empty history.
        var restarted = await registry.GetOrCreateAsync(
            Session("a"), Device("a"), TestContext.Current.CancellationToken);

        restarted.CreatedAt.ShouldBe(clock.Now);
        restarted.CreatedAt.ShouldNotBe(first.CreatedAt);
    }

    [Fact]
    public async Task 件数上限を超えたら_最終アクセスが最も古いセッションから破棄する()
    {
        var clock = new TestTimeProvider(Origin);
        var registry = new InMemorySessionRegistry(
            clock,
            // Use a long idle timeout so only the count limit is exercised.
            new TurnRuntimeOptions { MaxSessions = 2, SessionIdleTimeoutMinutes = 600 });

        var oldest = await registry.GetOrCreateAsync(
            Session("a"), Device("a"), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromMinutes(1));
        await registry.GetOrCreateAsync(
            Session("b"), Device("b"), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromMinutes(1));
        var newest = await registry.GetOrCreateAsync(
            Session("c"), Device("c"), TestContext.Current.CancellationToken);

        // Evict a, the least recently accessed session, when over the limit.
        clock.Advance(TimeSpan.FromMinutes(1));
        await registry.GetOrCreateAsync(
            Session("d"), Device("d"), TestContext.Current.CancellationToken);

        // Retain newer sessions as the same instances.
        var stillThere = await registry.GetOrCreateAsync(
            Session("c"), Device("c"), TestContext.Current.CancellationToken);

        stillThere.CreatedAt.ShouldBe(newest.CreatedAt);

        // Retrieving evicted session a creates a new instance.
        var restarted = await registry.GetOrCreateAsync(
            Session("a"), Device("a"), TestContext.Current.CancellationToken);

        restarted.CreatedAt.ShouldBe(clock.Now);
        restarted.CreatedAt.ShouldNotBe(oldest.CreatedAt);
    }

    [Fact]
    public async Task クリーンアップは_取得中のセッションロックを削除しない()
    {
        // Removing a held lock would let turns for the same session run concurrently under different locks.
        var harness = new TurnRuntimeHarness(
            maxConcurrentTurns: 2,
            maxSessions: 1,
            sessionIdleTimeoutMinutes: 1);

        // Also create an old, unheld lock that is eligible for cleanup.
        await harness.RunAsync(TurnRuntimeHarness.Request(device: "atoms3r-bbbbbbbbbbbb"));
        harness.SpeechToText.Calls.ShouldBe(1);

        var block = new TaskCompletionSource();
        harness.SpeechToText.Block = block;

        var running = harness.RunAsync();
        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.SpeechToText.Calls == 2, "ロックを保持するターンが音声認識を開始する");

        // Run cleanup while both count and idle limits are exceeded.
        harness.Runtime.Gates.EvictIdle(harness.Clock.Now + TimeSpan.FromHours(1));

        // A second turn for the same session cannot reach recognition while the lock is held.
        var second = harness.RunAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        harness.SpeechToText.Calls.ShouldBe(2);

        harness.SpeechToText.Block = null;
        block.SetResult();

        var first = await running;
        var events = await second;

        first[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
        harness.SpeechToText.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task 破棄済みのセッション_ID_は_別のデバイスでも再利用できる()
    {
        // Evicting a session also removes its DeviceId association.
        var clock = new TestTimeProvider(Origin);
        var registry = new InMemorySessionRegistry(
            clock,
            new TurnRuntimeOptions { MaxSessions = 1, SessionIdleTimeoutMinutes = 1 });

        await registry.GetOrCreateAsync(
            Session("s"), Device("atoms3r-001122334455"), TestContext.Current.CancellationToken);

        // While retained, reject the same session ID from a different device.
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await registry.GetOrCreateAsync(
                Session("s"),
                Device("atoms3r-999999999999"),
                TestContext.Current.CancellationToken));

        clock.Advance(TimeSpan.FromMinutes(2));
        await registry.GetOrCreateAsync(
            Session("other"), Device("other"), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(10));
        await registry.GetOrCreateAsync(
            Session("other"), Device("other"), TestContext.Current.CancellationToken);

        // After eviction, accept a request from another device as a new session.
        var restarted = await registry.GetOrCreateAsync(
            Session("s"), Device("atoms3r-999999999999"), TestContext.Current.CancellationToken);

        restarted.DeviceId.Value.ShouldBe("atoms3r-999999999999");
        restarted.CreatedAt.ShouldBe(clock.Now);
    }
}
