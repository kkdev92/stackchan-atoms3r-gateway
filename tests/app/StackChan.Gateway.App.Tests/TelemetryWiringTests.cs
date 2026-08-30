using System.Diagnostics.Metrics;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Telemetry;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies that telemetry is recorded with the reference application's DI configuration.
/// </summary>
/// <remarks>
/// SDK tests cover the measuring decorator itself. These tests verify that
/// <c>AddStackChanRuntime</c> registers that decorator on the real request path.
/// </remarks>
public sealed class TelemetryWiringTests
{
    [Fact]
    public async Task 参照アプリのエンドポイントを通した会話で_メトリクスを記録する()
    {
        var seen = new List<string>();
        var gate = new Lock();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Meter.Name == GatewayTelemetry.Name)
                {
                    active.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
        {
            lock (gate)
            {
                seen.Add(instrument.Name);
            }
        });

        listener.SetMeasurementEventCallback<int>((instrument, _, _, _) =>
        {
            lock (gate)
            {
                seen.Add(instrument.Name);
            }
        });

        listener.Start();

        await using var factory = new GatewayFactory();
        factory.SpeechToText.Result = "こんにちは、元気ですか";
        factory.Agent.Fragments = ["[happy]こんにちは、スタックちゃんです。"];
        factory.TextToSpeech.Result = new PcmAudio(
            new short[2500], PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);

        using var client = factory.CreateClient();
        using var request = DeviceRequest.Speech(conversation: "conv-9");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Read the response body to the end so the turn completes.
        await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        string[] recorded;

        lock (gate)
        {
            recorded = [.. seen];
        }

        recorded.ShouldContain("stackchan.turn.duration");
        recorded.ShouldContain("stackchan.turn.first_audio");
        recorded.ShouldContain("stackchan.turns.active");
    }
}
