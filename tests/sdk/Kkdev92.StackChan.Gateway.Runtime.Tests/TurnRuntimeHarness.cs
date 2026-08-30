using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Runtime.Sessions;
using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Kkdev92.StackChan.Gateway.TestKit;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>Creates silence in the format accepted by the device.</summary>
internal static class TestAudio
{
    public static PcmAudio Canonical(int samples = 1600) =>
        new(new short[samples], PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);
}

/// <summary>A test TimeProvider that can advance to an arbitrary time.</summary>
internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// A test harness for turn processing.
/// </summary>
/// <remarks>
/// Replaces every dependent service with a test double and performs no external communication.
/// </remarks>
internal sealed class TurnRuntimeHarness
{
    public TurnRuntimeHarness(
        int maxConcurrentTurns = 2,
        int turnTimeoutSeconds = 120,
        int maxSessions = 128,
        int sessionIdleTimeoutMinutes = 120)
    {
        Options = new TurnRuntimeOptions
        {
            MaxConcurrentTurns = maxConcurrentTurns,
            TurnTimeoutSeconds = turnTimeoutSeconds,
            MaxSessions = maxSessions,
            SessionIdleTimeoutMinutes = sessionIdleTimeoutMinutes,
            OnUnexpected = Unexpected.Add,
        };

        Sessions = new InMemorySessionRegistry(Clock, Options);
        Runtime = new TurnRuntime(
            Sessions,
            SpeechToText,
            Agent,
            TextToSpeech,
            Clock,
            Options);
    }

    /// <summary>The runtime settings used by the harness.</summary>
    public TurnRuntimeOptions Options { get; }

    /// <summary>Exceptions logged without being exposed to clients.</summary>
    public List<Exception> Unexpected { get; } = [];

    public FakeSpeechToText SpeechToText { get; } = new();

    public FakeAgent Agent { get; } = new();

    public FakeTextToSpeech TextToSpeech { get; } = new();

    public TestTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    public InMemorySessionRegistry Sessions { get; }

    public TurnRuntime Runtime { get; }

    /// <summary>Creates a turn request in the format received from a device.</summary>
    public static TurnRequest Request(
        string device = "atoms3r-001122334455",
        string? text = null,
        string boot = "BOOT0000000000000000000000",
        string conversation = "conv-1")
    {
        var deviceId = new DeviceId(device);
        var context = new DeviceTurnContext(deviceId, boot, conversation);

        return text is null
            ? TurnRequest.FromAudio(new SessionId(device), context, TestAudio.Canonical())
            : TurnRequest.FromText(new SessionId(device), context, text);
    }

    /// <summary>
    /// Collects every event returned by a turn.
    /// </summary>
    /// <remarks>
    /// When CancellationToken is omitted, uses xUnit's test token to prevent a failing test from hanging.
    /// </remarks>
    public async Task<List<TurnEvent>> RunAsync(
        TurnRequest? request = null,
        CancellationToken? cancellationToken = null)
    {
        var events = new List<TurnEvent>();
        var token = cancellationToken ?? TestContext.Current.CancellationToken;

        await foreach (var turnEvent in Runtime
            .ExecuteAsync(request ?? Request(), token)
            .ConfigureAwait(false))
        {
            events.Add(turnEvent);
        }

        return events;
    }

    /// <summary>
    /// Waits until a condition is satisfied.
    /// </summary>
    /// <remarks>
    /// Bounds the wait so a test cannot hang when the condition is never satisfied.
    /// </remarks>
    public static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"5 秒以内に期待する状態を確認できませんでした: {what}");
            }

            await Task.Delay(5, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }
}
