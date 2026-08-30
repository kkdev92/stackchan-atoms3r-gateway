using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>Verifies session isolation and turn concurrency control.</summary>
public sealed class SessionAndConcurrencyTests
{
    [Fact]
    public async Task 同じデバイスの_2_回目の要求は_同じセッションを使う()
    {
        var harness = new TurnRuntimeHarness();

        await harness.RunAsync();
        var created = await harness.Sessions.GetOrCreateAsync(
            new SessionId("atoms3r-001122334455"),
            new DeviceId("atoms3r-001122334455"),
            TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await harness.RunAsync();

        var second = await harness.Sessions.GetOrCreateAsync(
            new SessionId("atoms3r-001122334455"),
            new DeviceId("atoms3r-001122334455"),
            TestContext.Current.CancellationToken);

        second.CreatedAt.ShouldBe(created.CreatedAt);
        second.LastActivityAt.ShouldBe(harness.Clock.Now);
        second.LastActivityAt.ShouldBeGreaterThan(created.CreatedAt);
    }

    [Fact]
    public async Task 異なるデバイスは_別のセッションを使う()
    {
        var harness = new TurnRuntimeHarness();

        await harness.RunAsync(TurnRuntimeHarness.Request(device: "atoms3r-aaaaaaaaaaaa"));
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await harness.RunAsync(TurnRuntimeHarness.Request(device: "atoms3r-bbbbbbbbbbbb"));

        var first = await harness.Sessions.GetOrCreateAsync(
            new SessionId("atoms3r-aaaaaaaaaaaa"),
            new DeviceId("atoms3r-aaaaaaaaaaaa"),
            TestContext.Current.CancellationToken);
        var second = await harness.Sessions.GetOrCreateAsync(
            new SessionId("atoms3r-bbbbbbbbbbbb"),
            new DeviceId("atoms3r-bbbbbbbbbbbb"),
            TestContext.Current.CancellationToken);

        first.DeviceId.Value.ShouldBe("atoms3r-aaaaaaaaaaaa");
        second.DeviceId.Value.ShouldBe("atoms3r-bbbbbbbbbbbb");
        second.CreatedAt.ShouldBeGreaterThan(first.CreatedAt);
    }

    [Fact]
    public async Task 同じセッション_ID_を別のデバイスが使用したら_internal_エラーにする()
    {
        var harness = new TurnRuntimeHarness();
        await harness.RunAsync();

        // Send a request combining an existing SessionId with a different DeviceId.
        var request = TurnRequest.FromAudio(
            new SessionId("atoms3r-001122334455"),
            new DeviceTurnContext(new DeviceId("atoms3r-999999999999"), "BOOT", "conv-2"),
            TestAudio.Canonical());

        var events = await harness.RunAsync(request);

        events.OfType<TurnFailed>().ShouldHaveSingleItem()
            .Error.Code.ShouldBe(GatewayErrorCode.Internal);
    }

    [Fact]
    public async Task 同じセッションへの_2_つのターンは直列に処理する()
    {
        var harness = new TurnRuntimeHarness(maxConcurrentTurns: 2);
        var block = new TaskCompletionSource();
        harness.SpeechToText.Block = block;

        var first = harness.RunAsync();

        // Block the first turn in recognition and start a second meanwhile.
        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.SpeechToText.Calls == 1, "最初のターンが音声認識を開始する");

        var second = harness.RunAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // The session lock prevents the second turn from reaching recognition.
        harness.SpeechToText.Calls.ShouldBe(1);

        harness.SpeechToText.Block = null;
        block.SetResult();

        var events = await Task.WhenAll(first, second);
        events[0][^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
        events[1][^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
        harness.SpeechToText.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task 同時実行枠が埋まっていれば_プロバイダーを呼ばずに拒否する()
    {
        var harness = new TurnRuntimeHarness(maxConcurrentTurns: 1);
        var block = new TaskCompletionSource();
        harness.SpeechToText.Block = block;

        var first = harness.RunAsync();
        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.SpeechToText.Calls == 1, "最初のターンが音声認識を開始する");

        // Apply the global concurrency limit even to requests from different devices.
        var events = await harness.RunAsync(TurnRuntimeHarness.Request(device: "atoms3r-cccccccccccc"));

        var failed = events[0].ShouldBeOfType<TurnFailed>();
        failed.Error.Code.ShouldBe(GatewayErrorCode.Busy);
        failed.Error.Retryable.ShouldBeTrue();
        events[1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Failed);
        harness.SpeechToText.Calls.ShouldBe(1);

        harness.SpeechToText.Block = null;
        block.SetResult();
        await first;
    }

    [Fact]
    public async Task 失敗したターンの後に_次のターンを処理できる()
    {
        var harness = new TurnRuntimeHarness(maxConcurrentTurns: 1);
        harness.SpeechToText.Throws = new ProviderException(
            GatewayErrorCode.Unavailable, "stt down", true);

        var failedEvents = await harness.RunAsync();
        failedEvents.OfType<TurnFailed>().ShouldNotBeEmpty();

        harness.SpeechToText.Throws = null;
        var events = await harness.RunAsync();

        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
    }

    [Fact]
    public async Task キャンセルは_依存サービスへ伝播し_同時実行枠を解放する()
    {
        var harness = new TurnRuntimeHarness(maxConcurrentTurns: 1);
        var block = new TaskCompletionSource();
        harness.SpeechToText.Block = block;

        using var cancellation = new CancellationTokenSource();
        var running = harness.RunAsync(cancellationToken: cancellation.Token);

        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.SpeechToText.Calls == 1, "ターンが音声認識を開始する");

        await cancellation.CancelAsync();
        var events = await running;

        harness.SpeechToText.ObservedCancellation.ShouldBeTrue();
        events.OfType<TurnFailed>().ShouldHaveSingleItem()
            .Error.Code.ShouldBe(GatewayErrorCode.Cancelled);
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Cancelled);

        // The concurrency slot is released so the next turn can be admitted.
        harness.SpeechToText.Block = null;
        var next = await harness.RunAsync();
        next[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
    }

    [Fact]
    public async Task 音声合成の先読み中にキャンセルしても_処理を中断して同時実行枠を解放する()
    {
        // Cancel like a device disconnect while two sentences are being synthesized concurrently.
        var harness = new TurnRuntimeHarness(maxConcurrentTurns: 1);
        harness.Agent.Fragments =
        [
            "[happy]あさですね。",
            "[sad]よるですね。",
            "[neutral]おやすみなさい、また明日。",
        ];

        var block = new TaskCompletionSource();
        harness.TextToSpeech.Block = block;

        using var cancellation = new CancellationTokenSource();
        var running = harness.RunAsync(cancellationToken: cancellation.Token);

        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.TextToSpeech.MaxInFlight >= 2, "2 件の音声合成が並行して実行される");

        await cancellation.CancelAsync();
        var events = await running;

        harness.TextToSpeech.ObservedCancellation.ShouldBeTrue();
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Cancelled);

        // Confirm prefetch also ends and the next turn is not busy.
        harness.TextToSpeech.Block = null;
        var next = await harness.RunAsync();
        next[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
    }

    [Fact]
    public async Task 応答ストリーム途中のキャンセルも_エージェントへ伝播する()
    {
        var harness = new TurnRuntimeHarness();
        // Use a fragment longer than the formatter's seven-character suffix to finalize the first sentence.
        harness.Agent.Fragments = ["[happy]こんにちは。今日はいい天気ですね。", "[neutral]続きです。"];
        harness.Agent.BlockAfterFirstFragment = new TaskCompletionSource();

        using var cancellation = new CancellationTokenSource();
        var running = harness.RunAsync(cancellationToken: cancellation.Token);

        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.TextToSpeech.Calls > 0, "最初の文の音声合成が開始される");

        await cancellation.CancelAsync();
        var events = await running;

        harness.Agent.ObservedCancellation.ShouldBeTrue();
        events.OfType<ReplyAudioAvailable>().ShouldHaveSingleItem();
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Cancelled);
    }

    [Fact]
    public async Task ターンのタイムアウトを超えたら_処理を中断する()
    {
        // The device has no overall turn limit, and keep-alive prevents its inter-event timeout, so the
        // gateway must limit total duration.
        var harness = new TurnRuntimeHarness(turnTimeoutSeconds: 1);
        harness.Agent.Fragments = ["[happy]こんにちは。今日はいい天気ですね。", "[neutral]続きです。"];

        // Leave the block in place to reproduce a model response that never completes.
        harness.Agent.BlockAfterFirstFragment = new TaskCompletionSource();

        var events = await harness.RunAsync();

        // Propagate timeout to dependencies so no operation remains active.
        harness.Agent.ObservedCancellation.ShouldBeTrue();

        // Report this as timeout to the device because the caller did not cancel it.
        events.OfType<TurnFailed>().ShouldHaveSingleItem()
            .Error.Code.ShouldBe(GatewayErrorCode.Timeout);
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Failed);
    }
}
