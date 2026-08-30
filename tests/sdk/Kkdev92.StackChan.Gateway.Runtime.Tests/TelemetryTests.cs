using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Telemetry;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>
/// Verifies that turn metrics and traces are available through standard subscription APIs.
/// </summary>
/// <remarks>
/// Uses <c>MeterListener</c> and <c>ActivityListener</c> to observe through the same path as consumers.
/// </remarks>
[Collection(nameof(TelemetryTests))]
public sealed class TelemetryTests
{
    [Fact]
    public async Task 完了したターンの_所要時間と結果を記録する()
    {
        using var measured = new Measured();

        var events = await RunAsync(
        [
            new TranscriptAvailable("こんにちは"),
            new ReplyAudioAvailable("はい。", SpeechExpression.Neutral, Audio()),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        events.Count.ShouldBe(3);

        var turn = measured.Single("stackchan.turn.duration");
        turn.Value.ShouldBeGreaterThanOrEqualTo(0);
        turn.Tags["outcome"].ShouldBe("completed");

        // Record time to first audio separately from the complete turn.
        measured.Count("stackchan.turn.first_audio").ShouldBe(1);
    }

    [Fact]
    public async Task 失敗したターンも_結果ラベルつきで記録する()
    {
        using var measured = new Measured();

        await RunAsync(
        [
            new TurnFailed(new GatewayError(GatewayErrorCode.Unavailable, "down", true)),
            new TurnCompleted(TurnCompletionReason.Failed),
        ]);

        measured.Single("stackchan.turn.duration").Tags["outcome"].ShouldBe("failed");

        // Do not record time to first audio when no audio was produced.
        measured.Count("stackchan.turn.first_audio").ShouldBe(0);
    }

    [Fact]
    public async Task 音声を複数回生成しても_最初の_1_回だけ計測する()
    {
        using var measured = new Measured();

        await RunAsync(
        [
            new ReplyAudioAvailable("いち。", SpeechExpression.Neutral, Audio()),
            new ReplyAudioAvailable("に。", SpeechExpression.Neutral, Audio()),
            new ReplyAudioAvailable("さん。", SpeechExpression.Neutral, Audio()),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        measured.Count("stackchan.turn.first_audio").ShouldBe(1);
    }

    [Fact]
    public async Task 実行中ターン数は_完了後に元へ戻る()
    {
        using var measured = new Measured();

        await RunAsync([new TurnCompleted(TurnCompletionReason.Completed)]);

        // The +1 at start and -1 at completion sum to zero.
        measured.Sum("stackchan.turns.active").ShouldBe(0);
        measured.Count("stackchan.turns.active").ShouldBe(2);
    }

    [Fact]
    public async Task 応答ストリームを途中で破棄しても_実行中ターン数は元へ戻る()
    {
        // Decrement the instrument on device disconnect so no active turn remains counted.
        using var measured = new Measured();

        var runtime = new ObservedTurnRuntime(
            new ScriptedTurnRuntime(
            [
                new ReplyAudioAvailable("いち。", SpeechExpression.Neutral, Audio()),
                new ReplyAudioAvailable("に。", SpeechExpression.Neutral, Audio()),
                new TurnCompleted(TurnCompletionReason.Completed),
            ]),
            TimeProvider.System);

        await foreach (var _ in runtime.ExecuteAsync(
            Request(), TestContext.Current.CancellationToken))
        {
            // Read only the first event and dispose the stream.
            break;
        }

        measured.Sum("stackchan.turns.active").ShouldBe(0);
    }

    [Fact]
    public async Task ターンごとに_1_つのトレースを記録し_デバイス_ID_を含めない()
    {
        var activities = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GatewayTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };

        ActivitySource.AddActivityListener(listener);

        await RunAsync(
        [
            new ReplyAudioAvailable("はい。", SpeechExpression.Neutral, Audio()),
            new TurnCompleted(TurnCompletionReason.Completed),
        ]);

        var activity = activities.ShouldHaveSingleItem();
        activity.OperationName.ShouldBe("stackchan.turn");
        activity.Status.ShouldBe(ActivityStatusCode.Ok);
        activity.GetTagItem("stackchan.outcome").ShouldBe("completed");
        activity.GetTagItem("stackchan.spoke").ShouldBe(true);

        // Do not include high-cardinality device IDs in trace attributes.
        activity.Tags
            .Select(tag => tag.Value)
            .ShouldNotContain("atoms3r-001122334455");
    }

    private static PcmAudio Audio() =>
        new(new short[100], PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);

    private static TurnRequest Request() => TurnRequest.FromText(
        new SessionId("atoms3r-001122334455"),
        new DeviceTurnContext(new DeviceId("atoms3r-001122334455"), "boot", "conv"),
        "こんにちは");

    private static async Task<List<TurnEvent>> RunAsync(TurnEvent[] script)
    {
        var runtime = new ObservedTurnRuntime(
            new ScriptedTurnRuntime(script), TimeProvider.System);

        var seen = new List<TurnEvent>();

        await foreach (var turnEvent in runtime.ExecuteAsync(
            Request(), TestContext.Current.CancellationToken))
        {
            seen.Add(turnEvent);
        }

        return seen;
    }

    /// <summary>A test turn runner that returns the specified event sequence.</summary>
    private sealed class ScriptedTurnRuntime(TurnEvent[] script) : ITurnRuntime
    {
        public async IAsyncEnumerable<TurnEvent> ExecuteAsync(
            TurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var turnEvent in script)
            {
                await Task.Yield();

                cancellationToken.ThrowIfCancellationRequested();

                yield return turnEvent;
            }
        }
    }

    /// <summary>Collects measurements through MeterListener.</summary>
    private sealed class Measured : IDisposable
    {
        private readonly MeterListener _listener = new();

        private readonly Lock _gate = new();

        private readonly List<(string Name, double Value, Dictionary<string, string> Tags)>
            _seen = [];

        public Measured()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == GatewayTelemetry.Name)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<double>(Record);
            _listener.SetMeasurementEventCallback<int>(Record);
            _listener.SetMeasurementEventCallback<long>(Record);

            _listener.Start();
        }

        public (double Value, Dictionary<string, string> Tags) Single(string name)
        {
            lock (_gate)
            {
                var hits = _seen.Where(seen => seen.Name == name).ToArray();

                hits.Length.ShouldBe(1, $"{name} が {hits.Length} 件記録されました。");

                return (hits[0].Value, hits[0].Tags);
            }
        }

        public int Count(string name)
        {
            lock (_gate)
            {
                return _seen.Count(seen => seen.Name == name);
            }
        }

        public double Sum(string name)
        {
            lock (_gate)
            {
                return _seen.Where(seen => seen.Name == name).Sum(seen => seen.Value);
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record<T>(
            Instrument instrument,
            T value,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where T : struct
        {
            var copied = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                copied[tag.Key] = tag.Value?.ToString() ?? "";
            }

            lock (_gate)
            {
                _seen.Add((instrument.Name, Convert.ToDouble(value), copied));
            }
        }
    }
}
