using System.Diagnostics;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>Regression tests for the gateway's own processing cost.</summary>
/// <remarks>
/// <para>
/// External provider latency varies substantially, so test doubles isolate gateway-internal processing.
/// </para>
/// <para>
/// Besides environment-sensitive absolute time, these tests check how output and retained data scale
/// with input. This detects quadratic processing and resource leaks.
/// </para>
/// </remarks>
public sealed class PerformanceBudgetTests
{
    // Allow normal variance while detecting long waits and infinite loops.
    private const int TurnBudgetMs = 2000;

    [Fact]
    public async Task 外部通信のないターンは_2_秒以内に完了する()
    {
        await using var factory = Factory(sentences: 2);
        using var client = factory.CreateClient();

        // Exclude the first run to remove host initialization time from measurements.
        await TurnAsync(client);

        var watch = Stopwatch.StartNew();

        await TurnAsync(client);

        watch.ElapsedMilliseconds.ShouldBeLessThan(
            TurnBudgetMs,
            $"フェイクのみの構成でターンの処理時間が {TurnBudgetMs} ms の上限を超えました（{watch.ElapsedMilliseconds} ms）。");
    }

    [Fact]
    public async Task 文数が増えても_応答サイズはほぼ線形に増える()
    {
        // Short durations have high measurement error, so compare deterministic response sizes.
        // A regression that repeats sentences would make response size grow almost quadratically.
        var small = await ShapeAsync(sentences: 5);
        var large = await ShapeAsync(sentences: 40);

        // Sentence count grows eightfold; allow up to twelvefold response growth for fixed terminal events.
        var eventRatio = (double)large.Events / small.Events;

        eventRatio.ShouldBeLessThan(
            12.0,
            $"文数を 8 倍にしたとき SSE イベント数が {eventRatio:F1} 倍になった " +
            $"（5 文: {small.Events} 件、40 文: {large.Events} 件）");

        var byteRatio = (double)large.Bytes / small.Bytes;

        byteRatio.ShouldBeLessThan(
            12.0,
            $"文数を 8 倍にしたとき応答サイズが {byteRatio:F1} 倍になった " +
            $"（5 文: {small.Bytes} B、40 文: {large.Bytes} B）");

        // Also ensure the measured sentence count actually appears in the response.
        large.Events.ShouldBeGreaterThan(small.Events * 3);
    }

    [Fact]
    public async Task ターンを繰り返しても_保持メモリが増え続けない()
    {
        // IdleEviction bounds session, lock, and history caches. Verify retained memory does not keep
        // growing after enough executions.
        await using var factory = Factory(sentences: 1);
        using var client = factory.CreateClient();

        for (var index = 0; index < 10; index++)
        {
            await TurnAsync(client, device: $"warm-{index}");
        }

        var before = GC.GetTotalMemory(forceFullCollection: true);

        for (var index = 0; index < 60; index++)
        {
            await TurnAsync(client, device: $"many-{index}");
        }

        var after = GC.GetTotalMemory(forceFullCollection: true);
        var grew = (after - before) / 1024 / 1024;

        // Treat growth of 32 MiB or more over 60 turns as a cache or buffer leak.
        grew.ShouldBeLessThan(
            32, $"60 ターンで {grew} MiB 増えた（前 {before / 1024 / 1024} MiB → 後 {after / 1024 / 1024} MiB）");
    }

    private static async Task<(int Events, int Bytes)> ShapeAsync(int sentences)
    {
        await using var factory = Factory(sentences);
        using var client = factory.CreateClient();

        using var request = DeviceRequest.Text(
            "こんにちは", DeviceRequest.DefaultDevice, conversation: "shape");

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsByteArrayAsync(
            TestContext.Current.CancellationToken);

        return (SseWire.Events(body).Count, body.Length);
    }

    private static GatewayFactory Factory(int sentences)
    {
        var factory = new GatewayFactory();

        factory.SpeechToText.Result = "こんにちは";

        // Use a sentence longer than the streaming formatter's retained suffix.
        factory.Agent.Fragments = [.. Enumerable
            .Range(0, sentences)
            .Select(index => $"[neutral]これは{index}番目の文です、よろしくお願いします。")];

        factory.TextToSpeech.Result = new PcmAudio(
            new short[1600], PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);

        return factory;
    }

    private static async Task TurnAsync(HttpClient client, string? device = null)
    {
        using var request = DeviceRequest.Text(
            "こんにちは",
            device ?? DeviceRequest.DefaultDevice,
            conversation: "perf");

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        // Read the response body through completion to finish the turn.
        await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }
}
