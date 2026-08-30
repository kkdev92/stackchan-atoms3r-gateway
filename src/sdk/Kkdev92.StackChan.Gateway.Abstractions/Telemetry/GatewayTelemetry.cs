using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kkdev92.StackChan.Gateway.Abstractions.Telemetry;

/// <summary>Defines gateway metrics and distributed tracing.</summary>
/// <remarks>
/// <para>
/// To collect telemetry with OpenTelemetry, register <c>AddMeter("StackChan.Gateway")</c> and
/// <c>AddSource("StackChan.Gateway")</c> in the application.
/// </para>
/// <para>
/// Telemetry does not record audio, transcripts, prompts, or similar content. Device identifiers
/// are not used as metric attributes because they would create unbounded cardinality.
/// </para>
/// </remarks>
public static class GatewayTelemetry
{
    /// <summary>Gets the meter and activity source name.</summary>
    public const string Name = "StackChan.Gateway";

    private static readonly Meter Meter = new(Name, "0.1.0");

    /// <summary>Gets the activity source used to trace turns.</summary>
    public static readonly ActivitySource Source = new(Name, "0.1.0");

    private static readonly Histogram<double> TurnDuration = Meter.CreateHistogram<double>(
        "stackchan.turn.duration",
        "ms",
        "Turn duration. The outcome attribute is completed, failed, cancelled, or other.");

    private static readonly Histogram<double> FirstAudio = Meter.CreateHistogram<double>(
        "stackchan.turn.first_audio",
        "ms",
        "Time from user input to the first audio sent.");

    private static readonly UpDownCounter<int> ActiveTurns = Meter.CreateUpDownCounter<int>(
        "stackchan.turns.active",
        "{turn}",
        "Number of turns currently being processed.");

    private static readonly Histogram<double> ProviderDuration = Meter.CreateHistogram<double>(
        "stackchan.provider.duration",
        "ms",
        "Provider call duration, classified by the provider and outcome attributes.");

    private static readonly Counter<long> BreakerOpened = Meter.CreateCounter<long>(
        "stackchan.provider.breaker.opened",
        "{time}",
        "Number of times a provider circuit breaker opened.");

    private static readonly Counter<long> CapabilityCalls = Meter.CreateCounter<long>(
        "stackchan.capability.calls",
        "{call}",
        "Capability call count, classified by the capability and outcome attributes.");

    /// <summary>Records the duration of a completed turn.</summary>
    /// <param name="outcome">Bounded outcome name such as <c>completed</c> or <c>failed</c>.</param>
    /// <param name="elapsed">Turn duration.</param>
    public static void TurnEnded(string outcome, TimeSpan elapsed) =>
        TurnDuration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Records the time from user input to the first audio sent.</summary>
    /// <param name="elapsed">Time elapsed since the user input was received.</param>
    public static void FirstAudioSent(TimeSpan elapsed) =>
        FirstAudio.Record(elapsed.TotalMilliseconds);

    /// <summary>Changes the number of active turns.</summary>
    /// <param name="delta"><c>1</c> when a turn starts and <c>-1</c> when it ends.</param>
    public static void ActiveTurnsChanged(int delta) => ActiveTurns.Add(delta);

    /// <summary>Records the result of one provider call.</summary>
    /// <param name="provider">Bounded provider name such as <c>stt</c>, <c>tts</c>, or <c>model</c>.</param>
    /// <param name="outcome">Bounded outcome name such as <c>ok</c>, <c>failed</c>, or <c>rejected</c>.</param>
    /// <param name="elapsed">Call duration.</param>
    public static void ProviderCalled(string provider, string outcome, TimeSpan elapsed) =>
        ProviderDuration.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Records that a provider circuit breaker opened.</summary>
    /// <param name="provider">Bounded provider name.</param>
    public static void BreakerOpenedFor(string provider) =>
        BreakerOpened.Add(1, new KeyValuePair<string, object?>("provider", provider));

    /// <summary>Records the result of a capability call.</summary>
    /// <param name="capability">Unique capability invocation name.</param>
    /// <param name="outcome">Bounded outcome name such as <c>ok</c> or <c>failed</c>.</param>
    public static void CapabilityCalled(string capability, string outcome) =>
        CapabilityCalls.Add(
            1,
            new KeyValuePair<string, object?>("capability", capability),
            new KeyValuePair<string, object?>("outcome", outcome));
}
