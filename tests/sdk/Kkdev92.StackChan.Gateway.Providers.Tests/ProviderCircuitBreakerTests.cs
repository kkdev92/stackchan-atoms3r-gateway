using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Providers.Http;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Providers.Tests;

/// <summary>
/// Verifies that the circuit breaker suppresses repeated calls to a failing provider.
/// </summary>
/// <remarks>
/// Models an unresponsive endpoint that makes each turn wait for a timeout. After a configured number
/// of failures, calls are blocked and only a recovery probe is allowed through.
/// </remarks>
public sealed class ProviderCircuitBreakerTests
{
    private static ProviderException Down() =>
        ProviderEndpoint.Unavailable("provider is unreachable");

    private static ProviderException Misconfigured() =>
        ProviderEndpoint.Unavailable("provider rejected the request", retryable: false);

    [Fact]
    public async Task 成功が続く間は_すべての呼び出しを通す()
    {
        var breaker = new ProviderCircuitBreaker("stt");
        var calls = 0;

        for (var index = 0; index < 10; index++)
        {
            var result = await breaker.RunAsync(
                _ => { calls++; return Task.FromResult(42); },
                "unavailable",
                TestContext.Current.CancellationToken);

            result.ShouldBe(42);
        }

        calls.ShouldBe(10);
        breaker.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task 連続失敗が閾値に達したら_後続呼び出しを遮断する()
    {
        var time = new TestClock();
        var breaker = new ProviderCircuitBreaker("stt", threshold: 3, timeProvider: time);
        var calls = 0;

        // Call the provider until the failure count reaches the threshold.
        for (var index = 0; index < 3; index++)
        {
            await Should.ThrowAsync<ProviderException>(() => breaker.RunAsync<int>(
                _ => { calls++; throw Down(); },
                "unavailable",
                TestContext.Current.CancellationToken));
        }

        calls.ShouldBe(3);
        breaker.IsOpen.ShouldBeTrue();

        // Do not call the provider after reaching the threshold.
        var refused = await Should.ThrowAsync<ProviderException>(() => breaker.RunAsync<int>(
            _ => { calls++; throw Down(); },
            "speech synthesis is unavailable",
            TestContext.Current.CancellationToken));

        calls.ShouldBe(3, "サーキットの開放中にプロバイダーが呼び出されました。");
        refused.Code.ShouldBe(GatewayErrorCode.Unavailable);
        refused.Retryable.ShouldBeTrue("呼び出し側が再試行できるエラーとして返す必要があります。");
        refused.Message.ShouldBe("speech synthesis is unavailable");
    }

    [Fact]
    public async Task 途中で成功したら_連続失敗回数をリセットする()
    {
        var time = new TestClock();
        var breaker = new ProviderCircuitBreaker("stt", threshold: 3, timeProvider: time);

        await FailAsync(breaker);
        await FailAsync(breaker);

        // Count the next failure as the first after a success.
        await breaker.RunAsync(
            _ => Task.FromResult(1), "unavailable", TestContext.Current.CancellationToken);

        await FailAsync(breaker);
        await FailAsync(breaker);

        breaker.IsOpen.ShouldBeFalse("成功後の連続失敗は 2 回であり、開放条件に達していません。");
    }

    [Fact]
    public async Task 開放期間が過ぎたら_回復確認の_1_件だけを通す()
    {
        var time = new TestClock();
        var breaker = new ProviderCircuitBreaker(
            "stt", threshold: 1, openFor: TimeSpan.FromSeconds(15), timeProvider: time);

        await FailAsync(breaker);
        breaker.IsOpen.ShouldBeTrue();

        // Block calls during the open interval.
        time.Advance(TimeSpan.FromSeconds(14));
        breaker.IsOpen.ShouldBeTrue();

        // After the open interval, enter half-open state and allow one recovery probe.
        time.Advance(TimeSpan.FromSeconds(2));
        breaker.IsOpen.ShouldBeFalse();

        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource<int>();

        var probe = breaker.RunAsync(
            async _ =>
            {
                started.SetResult();

                return await release.Task;
            },
            "unavailable",
            TestContext.Current.CancellationToken);

        await started.Task;

        // Do not allow another call while a recovery probe is running.
        var second = 0;
        await Should.ThrowAsync<ProviderException>(() => breaker.RunAsync(
            _ => { second++; return Task.FromResult(1); },
            "unavailable",
            TestContext.Current.CancellationToken));

        second.ShouldBe(0, "半開状態では、回復確認の呼び出しを同時に 1 件だけ許可します。");

        release.SetResult(7);
        (await probe).ShouldBe(7);

        // Close the circuit after a successful recovery probe.
        breaker.IsOpen.ShouldBeFalse();
        await breaker.RunAsync(
            _ => Task.FromResult(1), "unavailable", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 回復確認が失敗したら_失敗回数を数え直さず再び開く()
    {
        var time = new TestClock();
        var breaker = new ProviderCircuitBreaker(
            "stt", threshold: 3, openFor: TimeSpan.FromSeconds(15), timeProvider: time);

        await FailAsync(breaker);
        await FailAsync(breaker);
        await FailAsync(breaker);
        breaker.IsOpen.ShouldBeTrue();

        time.Advance(TimeSpan.FromSeconds(16));

        // If the half-open probe fails, reopen immediately without waiting for the threshold again.
        await FailAsync(breaker);

        breaker.IsOpen.ShouldBeTrue("回復確認が失敗した時点で、サーキットを再び開放する必要があります。");
    }

    [Fact]
    public async Task 再試行不能なエラーは_連続失敗へ数えない()
    {
        var time = new TestClock();
        var breaker = new ProviderCircuitBreaker("stt", threshold: 2, timeProvider: time);

        // Do not treat failures requiring configuration changes as transient; keep returning the original error.
        for (var index = 0; index < 5; index++)
        {
            await Should.ThrowAsync<ProviderException>(() => breaker.RunAsync<int>(
                _ => throw Misconfigured(),
                "unavailable",
                TestContext.Current.CancellationToken));
        }

        breaker.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task 呼び出し元からのキャンセルは_連続失敗へ数えない()
    {
        var time = new TestClock();
        var breaker = new ProviderCircuitBreaker("stt", threshold: 2, timeProvider: time);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        for (var index = 0; index < 5; index++)
        {
            await Should.ThrowAsync<OperationCanceledException>(() => breaker.RunAsync<int>(
                token => Task.FromCanceled<int>(token),
                "unavailable",
                cancelled.Token));
        }

        breaker.IsOpen.ShouldBeFalse("呼び出し元によるキャンセルは、プロバイダー障害として数えません。");
    }

    [Fact]
    public async Task 未変換の例外も_一時障害として数える()
    {
        // Protect callers from repeated failures even when an exception was not converted to ProviderException.
        var time = new TestClock();
        var breaker = new ProviderCircuitBreaker("stt", threshold: 2, timeProvider: time);

        for (var index = 0; index < 2; index++)
        {
            await Should.ThrowAsync<InvalidOperationException>(() => breaker.RunAsync<int>(
                _ => throw new InvalidOperationException("翻訳し忘れ"),
                "unavailable",
                TestContext.Current.CancellationToken));
        }

        breaker.IsOpen.ShouldBeTrue();
    }

    /// <summary>
    /// A test TimeProvider that can advance to an arbitrary time.
    /// </summary>
    /// <remarks>
    /// Used to verify the circuit's open interval without waiting in real time.
    /// </remarks>
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static async Task FailAsync(ProviderCircuitBreaker breaker) =>
        await Should.ThrowAsync<ProviderException>(() => breaker.RunAsync<int>(
            _ => throw Down(), "unavailable", TestContext.Current.CancellationToken));
}
