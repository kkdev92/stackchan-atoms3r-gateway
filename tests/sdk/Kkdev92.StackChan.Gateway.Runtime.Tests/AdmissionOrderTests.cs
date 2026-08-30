using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>
/// Verifies that requests denied a concurrency slot do not create sessions.
/// </summary>
/// <remarks>
/// If concurrency is checked after session registration, requests rejected as <c>busy</c> still consume
/// the session limit. Rejected requests must not remain in the registry even when many distinct device IDs arrive.
/// </remarks>
public sealed class AdmissionOrderTests
{
    [Fact]
    public async Task busy_で拒否した要求は_セッションを登録しない()
    {
        // Set concurrency to one and block the first turn so later requests become busy.
        var harness = new TurnRuntimeHarness(maxConcurrentTurns: 1);
        var blocked = new TaskCompletionSource();

        harness.Agent.BlockAfterFirstFragment = blocked;

        var first = harness.RunAsync(TurnRuntimeHarness.Request(device: "held"));

        // Wait for the first turn to acquire the concurrency slot.
        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.Sessions.Count >= 1, "最初のターンが同時実行枠を取得する");

        var before = harness.Sessions.Count;

        // Send requests with distinct device IDs and reject all of them as busy.
        for (var index = 0; index < 20; index++)
        {
            var events = await harness.RunAsync(
                TurnRuntimeHarness.Request(device: $"rejected-{index}"));

            events.OfType<TurnFailed>().ShouldHaveSingleItem()
                .Error.Code.ShouldBe(GatewayErrorCode.Busy);
        }

        harness.Sessions.Count.ShouldBe(
            before,
            $"拒否した要求がセッションへ登録された (前={before} 後={harness.Sessions.Count})");

        blocked.SetResult();
        await first;
    }

    [Fact]
    public async Task 受け入れた要求は_セッションを登録する()
    {
        // Only the request admitted to a concurrency slot creates a session.
        var harness = new TurnRuntimeHarness();

        await harness.RunAsync(TurnRuntimeHarness.Request(device: "accepted-1"));
        await harness.RunAsync(TurnRuntimeHarness.Request(device: "accepted-2"));

        harness.Sessions.Count.ShouldBe(2);
    }
}
