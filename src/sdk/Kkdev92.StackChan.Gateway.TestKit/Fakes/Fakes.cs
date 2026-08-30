using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;

namespace Kkdev92.StackChan.Gateway.TestKit;

/// <summary>
/// Manages call state and behavior for test fakes.
/// </summary>
/// <remarks>
/// Calls can be blocked at a chosen point to test cancellation and exception handling.
/// </remarks>
public abstract class FakeCall
{
    /// <summary>Gets the number of calls.</summary>
    public int Calls { get; private set; }

    /// <summary>Gets or sets the exception thrown on a call.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Gets or sets the gate that blocks a call.</summary>
    public TaskCompletionSource? Block { get; set; }

    /// <summary>Gets whether cancellation was observed while waiting.</summary>
    public bool ObservedCancellation { get; protected set; }

    /// <summary>Records a call and applies configured blocking, cancellation, and exception behavior.</summary>
    protected async Task EnterAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Block is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Throws is not null)
        {
            throw Throws;
        }
    }
}

/// <summary>A speech recognition fake that returns a configured transcript.</summary>
public sealed class FakeSpeechToText : FakeCall, ISpeechToText
{
    /// <summary>Gets or sets the transcript to return.</summary>
    public string Result { get; set; } = "こんにちは";

    /// <inheritdoc />
    public async Task<Transcript> TranscribeAsync(
        PcmAudio audio,
        CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);

        return new Transcript(Result);
    }
}

/// <summary>A speech synthesis fake that returns configured audio.</summary>
public sealed class FakeTextToSpeech : FakeCall, ITextToSpeech
{
    /// <summary>Gets requested synthesis text in call order.</summary>
    public List<string> Texts { get; } = [];

    /// <summary>Gets or sets the audio to return.</summary>
    public PcmAudio Result { get; set; } =
        new(new short[] { 1, 2, 3, 4 }, PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);

    /// <summary>Gets or sets the one-based call number at which failures begin. A value of 0 disables failures.</summary>
    public int FailFrom { get; set; }

    /// <summary>Gets the maximum number of synthesis operations observed concurrently.</summary>
    public int MaxInFlight { get; private set; }

    private int _inFlight;

    /// <inheritdoc />
    public async Task<PcmAudio> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        Texts.Add(text);

        var running = Interlocked.Increment(ref _inFlight);

        if (running > MaxInFlight)
        {
            MaxInFlight = running;
        }

        if (FailFrom > 0 && Texts.Count >= FailFrom)
        {
            Throws = new ProviderException(
                GatewayErrorCode.Unavailable, "tts down", retryable: true);
        }

        try
        {
            await EnterAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }

        return Result;
    }
}

/// <summary>An agent fake that returns configured fragments in order.</summary>
/// <remarks>
/// Blocking immediately after the first fragment allows tests to reproduce midstream cancellation.
/// </remarks>
public sealed class FakeAgent : FakeCall, IAgent
{
    /// <summary>Gets or sets the text fragments to return.</summary>
    public IReadOnlyList<string> Fragments { get; set; } = ["こんにちは。"];

    /// <summary>Gets received requests in call order.</summary>
    public List<AgentRequest> Requests { get; } = [];

    /// <summary>Gets or sets the gate that blocks processing after the first fragment.</summary>
    public TaskCompletionSource? BlockAfterFirstFragment { get; set; }

    /// <summary>Gets or sets whether an exception is thrown after all fragments are returned.</summary>
    public bool ThrowsAfterFragments { get; set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        AgentRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(request);
        await EnterAsync(cancellationToken).ConfigureAwait(false);

        var first = true;

        foreach (var fragment in Fragments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return fragment;

            if (first && BlockAfterFirstFragment is { } gate)
            {
                first = false;

                try
                {
                    await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    ObservedCancellation = true;
                    throw;
                }
            }
        }

        if (ThrowsAfterFragments)
        {
            throw new ProviderException(
                GatewayErrorCode.Unavailable, "agent down", retryable: true);
        }
    }
}

/// <summary>
/// A turn runtime that returns configured events in order.
/// </summary>
/// <remarks>
/// This fake allows tests to exercise only the conversion from turn events to protocol responses.
/// </remarks>
public sealed class FakeTurnRuntime : ITurnRuntime
{
    /// <summary>Gets the events to return.</summary>
    public List<TurnEvent> Events { get; } = [];

    /// <summary>Gets received requests in call order.</summary>
    public List<TurnRequest> Requests { get; } = [];

    /// <summary>Gets or sets the gate that blocks processing before the first event.</summary>
    public TaskCompletionSource? BlockBeforeFirstEvent { get; set; }

    /// <summary>Gets whether cancellation was observed while waiting.</summary>
    public bool ObservedCancellation { get; private set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<TurnEvent> ExecuteAsync(
        TurnRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (BlockBeforeFirstEvent is { } gate)
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }

        foreach (var turnEvent in Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return turnEvent;
        }
    }
}
