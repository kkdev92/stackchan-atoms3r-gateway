using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Capabilities.Tests;

/// <summary>
/// Verifies rules for converting capability calls to text responses usable in a conversation.
/// </summary>
/// <remarks>
/// External service failures and timeouts become fallback text so the turn can continue. Only
/// caller cancellation propagates to the runtime.
/// </remarks>
public sealed class CapabilityCallTests
{
    private const string Unavailable = "取得できませんでした。";

    private static Task<string> Answer(
        Func<CancellationToken, Task<string>> work,
        TimeSpan? timeout = null,
        CancellationToken? cancellationToken = null) =>
        CapabilityCall.AnswerAsync(
            work,
            Unavailable,
            timeout ?? TimeSpan.FromSeconds(10),
            cancellationToken ?? TestContext.Current.CancellationToken,
            logger: null,
            name: "probe");

    [Fact]
    public async Task 成功したら_Capability_の応答を返す()
    {
        var answer = await Answer(_ => Task.FromResult("晴れです。"));

        answer.ShouldBe("晴れです。");
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(System.Text.Json.JsonException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NotSupportedException))]
    public async Task 例外が発生したら_フォールバック文を返す(Type failure)
    {
        // Do not let an exception from a capability implementation fail the entire turn.
        var answer = await Answer(_ =>
            Task.FromException<string>((Exception)Activator.CreateInstance(failure)!));

        answer.ShouldBe(Unavailable);
    }

    [Fact]
    public async Task タイムアウトしたら_フォールバック文を返す()
    {
        var answer = await Answer(
            async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return "遅い答え";
            },
            timeout: TimeSpan.FromMilliseconds(100));

        answer.ShouldBe(Unavailable);
    }

    [Fact]
    public async Task 呼び出し元からのキャンセルは_そのまま伝播する()
    {
        // Distinguish caller cancellation from an internal timeout so the runtime can report Cancelled.
        using var cancellation = new CancellationTokenSource();

        var running = Answer(
            async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return "答え";
            },
            cancellationToken: cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task Capability_へ_タイムアウトつきの_CancellationToken_を渡す()
    {
        // Apply timeout to the token so asynchronous capability work is interrupted by the deadline.
        CancellationToken seen = default;

        await Answer(
            token =>
            {
                seen = token;
                return Task.FromResult("ok");
            },
            timeout: TimeSpan.FromMilliseconds(50));

        seen.CanBeCanceled.ShouldBeTrue();
    }

    [Fact]
    public async Task フォールバック文が空なら_構築時に拒否する()
    {
        // Return response text to the device even when a capability fails.
        await Should.ThrowAsync<ArgumentException>(() =>
            CapabilityCall.AnswerAsync(
                _ => Task.FromResult("ok"),
                "",
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken));
    }
}
