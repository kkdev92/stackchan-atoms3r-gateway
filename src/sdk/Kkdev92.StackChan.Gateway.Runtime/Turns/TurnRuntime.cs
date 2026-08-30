using System.Runtime.CompilerServices;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Sessions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Runtime.Concurrency;
using Kkdev92.StackChan.Gateway.Runtime.Sessions;
using Kkdev92.StackChan.Gateway.Runtime.Text;

namespace Kkdev92.StackChan.Gateway.Runtime.Turns;

/// <summary>
/// Runs speech recognition, agent response generation, text shaping, and speech synthesis as one turn.
/// </summary>
/// <remarks>
/// The runtime is independent of HTTP and SSE and returns results as a stream of
/// <see cref="TurnEvent"/> values. Downstream failures are converted to <see cref="TurnFailed"/>
/// and <see cref="TurnCompleted"/>, allowing callers to finish a turn without exposing exception
/// details to clients.
/// </remarks>
public sealed class TurnRuntime : ITurnRuntime, IDisposable
{
    private readonly ISessionRegistry _sessions;
    private readonly ISpeechToText _speechToText;
    private readonly IAgent _agent;
    private readonly ITextToSpeech _textToSpeech;
    private readonly TimeProvider _timeProvider;
    private readonly TurnConcurrencyGate _gate;
    private readonly TimeSpan _turnTimeout;
    private readonly Action<Exception>? _onUnexpected;

    /// <summary>Locks that serialize turns in the same session.</summary>
    private readonly SessionGates _sessionGates;

    /// <summary>Gets the per-session locks.</summary>
    internal SessionGates Gates => _sessionGates;

    /// <summary>Initializes the runtime with the services and settings required to execute turns.</summary>
    /// <param name="sessions">The session registry that manages conversation state.</param>
    /// <param name="speechToText">The speech recognition service.</param>
    /// <param name="agent">The agent that generates responses.</param>
    /// <param name="textToSpeech">The speech synthesis service.</param>
    /// <param name="timeProvider">The provider used for timeouts and session timestamps.</param>
    /// <param name="options">The concurrency, timeout, and session management settings.</param>
    public TurnRuntime(
        ISessionRegistry sessions,
        ISpeechToText speechToText,
        IAgent agent,
        ITextToSpeech textToSpeech,
        TimeProvider timeProvider,
        TurnRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sessions = sessions;
        _speechToText = speechToText;
        _agent = agent;
        _textToSpeech = textToSpeech;
        _timeProvider = timeProvider;
        _gate = new TurnConcurrencyGate(options.MaxConcurrentTurns);
        _turnTimeout = TimeSpan.FromSeconds(options.TurnTimeoutSeconds);
        _onUnexpected = options.OnUnexpected;
        _sessionGates = new SessionGates(
            timeProvider,
            options.MaxSessions,
            TimeSpan.FromMinutes(options.SessionIdleTimeoutMinutes));
    }

    /// <summary>Holds cancellation tokens for turn processing and client disconnection.</summary>
    /// <remarks>
    /// The original disconnection token is kept separately so client disconnections can be treated
    /// as cancellation while runtime or provider deadlines are treated as timeouts.
    /// </remarks>
    private readonly record struct TurnScope(
        CancellationToken Work,
        CancellationToken Aborted);

    /// <inheritdoc />
    public async IAsyncEnumerable<TurnEvent> ExecuteAsync(
        TurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate canonical PCM input before passing audio to a provider.
        if (request.UserText is null &&
            (request.Audio.Samples.IsEmpty || !request.Audio.IsCanonical))
        {
            yield return new TurnFailed(TurnErrorMapper.Unexpected);
            yield return new TurnCompleted(TurnCompletionReason.Failed);
            yield break;
        }

        _sessionGates.EvictIdle(_timeProvider.GetUtcNow());

        // Apply an overall deadline so keep-alive events cannot extend a turn indefinitely.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_turnTimeout);

        var scope = new TurnScope(deadline.Token, cancellationToken);

        // Enforce admission before session lookup so rejected requests do not create sessions.
        if (!_gate.TryEnter())
        {
            yield return new TurnFailed(TurnErrorMapper.Busy);
            yield return new TurnCompleted(TurnCompletionReason.Failed);
            yield break;
        }

        try
        {
            var (_, sessionError) = await TryAsync(
                () => _sessions
                    .GetOrCreateAsync(request.SessionId, request.Device.DeviceId, scope.Work)
                    .AsTask(),
                scope).ConfigureAwait(false);

            if (sessionError is not null)
            {
                yield return new TurnFailed(sessionError);
                yield return new TurnCompleted(ReasonFor(sessionError));
                yield break;
            }

            // Serialize turns in the same session to preserve conversation history order.
            var (sessionGate, waitError) = await TryAsync(
                () => _sessionGates.AcquireAsync(request.SessionId, scope.Work),
                scope).ConfigureAwait(false);

            if (waitError is not null)
            {
                yield return new TurnFailed(waitError);
                yield return new TurnCompleted(ReasonFor(waitError));
                yield break;
            }

            try
            {
                await foreach (var (turnEvent, error) in RunAsync(request, scope)
                    .ConfigureAwait(false))
                {
                    if (error is not null)
                    {
                        yield return new TurnFailed(error);
                        yield return new TurnCompleted(ReasonFor(error));
                        yield break;
                    }

                    yield return turnEvent!;
                }
            }
            finally
            {
                sessionGate!.Gate.Release();
            }
        }
        finally
        {
            _gate.Leave();
        }
    }

    /// <summary>Runs turn processing after acquiring a concurrency slot and session lock.</summary>
    /// <returns>An event to send or an error that ends the turn. Only one tuple element is set.</returns>
    private async IAsyncEnumerable<(TurnEvent? Event, GatewayError? Error)> RunAsync(
        TurnRequest request,
        TurnScope scope)
    {
        string heard;
        if (request.UserText is { Length: > 0 } spoken)
        {
            heard = spoken;
        }
        else
        {
            var (transcript, sttError) = await TryAsync(
                () => _speechToText.TranscribeAsync(request.Audio, scope.Work),
                scope).ConfigureAwait(false);

            if (sttError is not null)
            {
                yield return (null, sttError);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(transcript!.Text))
            {
                yield return (null, TurnErrorMapper.SpeechRecognitionFailed);
                yield break;
            }

            heard = transcript.Text;
        }

        yield return (new TranscriptAvailable(heard), null);

        var agentRequest = new AgentRequest(request.SessionId, request.Device.DeviceId, heard);
        var sentenceCount = 0;
        var voicedCount = 0;

        // Synthesize one sentence ahead to reduce pauses while preserving event order.
        Task<(ReplyAudioAvailable? Event, GatewayError? Error)>? pending = null;

        await foreach (var (sentence, agentError) in SentencesAsync(agentRequest, scope)
            .ConfigureAwait(false))
        {
            if (agentError is not null)
            {
                // Send a synthesized sentence even if the agent fails afterward.
                if (pending is not null)
                {
                    var (earlier, earlierError) = await pending.ConfigureAwait(false);

                    if (earlierError is null && earlier is not null)
                    {
                        Count(earlier, ref sentenceCount, ref voicedCount);
                        yield return (earlier, null);
                    }
                }

                yield return (null, agentError);
                yield break;
            }

            var next = SpeakAsync(sentence!, scope).AsTask();

            if (pending is null)
            {
                pending = next;
                continue;
            }

            var (spokenEvent, speakError) = await pending.ConfigureAwait(false);
            pending = next;

            if (speakError is not null)
            {
                // Complete prefetched synthesis so resources such as HTTP responses are released.
                await Observe(pending).ConfigureAwait(false);

                yield return (null, speakError);
                yield break;
            }

            if (spokenEvent is not null)
            {
                Count(spokenEvent, ref sentenceCount, ref voicedCount);
                yield return (spokenEvent, null);
            }
        }

        if (pending is not null)
        {
            var (spokenEvent, speakError) = await pending.ConfigureAwait(false);

            if (speakError is not null)
            {
                yield return (null, speakError);
                yield break;
            }

            if (spokenEvent is not null)
            {
                Count(spokenEvent, ref sentenceCount, ref voicedCount);
                yield return (spokenEvent, null);
            }
        }

        if (sentenceCount == 0)
        {
            yield return (null, TurnErrorMapper.NoReply);
            yield break;
        }

        if (voicedCount == 0)
        {
            // End with an error if every synthesis attempt failed, even if text was already sent.
            yield return (null, TurnErrorMapper.NoVoice);
            yield break;
        }

        var touchError = await TryAsync(
            () => _sessions
                .TouchAsync(request.SessionId, _timeProvider.GetUtcNow(), scope.Work)
                .AsTask(),
            scope).ConfigureAwait(false);

        if (touchError is not null)
        {
            yield return (null, touchError);
            yield break;
        }

        yield return (new TurnCompleted(TurnCompletionReason.Completed), null);
    }

    /// <summary>Tracks generated sentences and successful speech synthesis operations.</summary>
    private static void Count(
        ReplyAudioAvailable spoken,
        ref int sentenceCount,
        ref int voicedCount)
    {
        sentenceCount++;

        if (!spoken.Audio.Samples.IsEmpty)
        {
            voicedCount++;
        }
    }

    /// <summary>Waits for prefetched speech synthesis to finish.</summary>
    /// <remarks>
    /// The result is ignored, but the task is awaited so resources such as HTTP responses held by
    /// the speech provider are released.
    /// </remarks>
    private static async Task Observe(
        Task<(ReplyAudioAvailable? Event, GatewayError? Error)> pending) =>
        await pending.ConfigureAwait(false);

    /// <summary>Shapes agent output fragments and returns sentences in speaking order.</summary>
    /// <remarks>
    /// Tags and whitespace across fragment boundaries are handled consistently, so the same content
    /// produces the same sentences regardless of model chunking. A final unterminated fragment is
    /// returned as the last sentence.
    /// </remarks>
    private async IAsyncEnumerable<(string? Sentence, GatewayError? Error)> SentencesAsync(
        AgentRequest request,
        TurnScope scope)
    {
        var sanitizer = new SpeechTextSanitizer();
        var assembler = new SentenceAssembler();

        var fragments = _agent.StreamAsync(request, scope.Work)
            .GetAsyncEnumerator(scope.Work);

        try
        {
            while (true)
            {
                var (moved, error) = await TryAsync(
                    async () => await fragments.MoveNextAsync().ConfigureAwait(false),
                    scope).ConfigureAwait(false);

                if (error is not null)
                {
                    yield return (null, error);
                    yield break;
                }

                if (!moved)
                {
                    break;
                }

                foreach (var sentence in assembler.Push(sanitizer.Push(fragments.Current)))
                {
                    foreach (var piece in SplitAtMarkers(sentence))
                    {
                        yield return (piece, null);
                    }
                }
            }
        }
        finally
        {
            await fragments.DisposeAsync().ConfigureAwait(false);
        }

        // Speak a final response even when it has no sentence terminator.
        var tail = sanitizer.Flush();
        if (tail.Length > 0)
        {
            foreach (var sentence in assembler.Push(tail))
            {
                foreach (var piece in SplitAtMarkers(sentence))
                {
                    yield return (piece, null);
                }
            }
        }

        if (assembler.Flush() is { } last)
        {
            foreach (var piece in SplitAtMarkers(last))
            {
                yield return (piece, null);
            }
        }
    }

    /// <summary>Splits a sentence at expression markers within the text.</summary>
    /// <remarks>
    /// The device also treats expression markers as sentence boundaries. Splitting synthesis at the
    /// same positions keeps expression changes aligned with audio segments.
    /// </remarks>
    private static IEnumerable<string> SplitAtMarkers(string sentence)
    {
        var start = 0;

        for (var index = 1; index < sentence.Length; index++)
        {
            if (sentence[index] != '[')
            {
                continue;
            }

            if (!ExpressionMarkers.All.Any(marker =>
                sentence.AsSpan(index).StartsWith(marker, StringComparison.Ordinal)))
            {
                continue;
            }

            var piece = sentence[start..index];

            if (piece.Length > 0)
            {
                yield return piece;
            }

            start = index;
        }

        if (start < sentence.Length)
        {
            yield return sentence[start..];
        }
    }

    /// <summary>Synthesizes one sentence and creates the event to send.</summary>
    /// <remarks>
    /// If synthesis fails for one sentence, its text is returned without audio and later sentences
    /// are still processed. A client disconnection or overall turn timeout stops processing.
    /// </remarks>
    /// <returns>The event to send and an error that ends the turn. Both are <see langword="null"/> when there is no text to speak.</returns>
    private async ValueTask<(ReplyAudioAvailable? Event, GatewayError? Error)> SpeakAsync(
        string rawSentence,
        TurnScope scope)
    {
        var sentence = ExpressionMarkers.Ensure(rawSentence);
        var expression = ReadExpression(sentence);
        var speakable = SentenceAssembler.StripMarkers(sentence);

        if (speakable.Length == 0)
        {
            return (null, null);
        }

        var (audio, error) = await TryAsync(
            () => _textToSpeech.SynthesizeAsync(speakable, scope.Work),
            scope).ConfigureAwait(false);

        // Propagate turn cancellation; treat an individual provider failure as missing audio.
        if (error is { Code: GatewayErrorCode.Cancelled } || scope.Work.IsCancellationRequested)
        {
            return (null, error);
        }

        var voice = audio ?? PcmAudio.Silence;

        if (!voice.IsCanonical)
        {
            return (null, new GatewayError(
                GatewayErrorCode.Internal,
                "the voice provider returned an unsupported audio format",
                Retryable: false));
        }

        return (new ReplyAudioAvailable(speakable, expression, voice), null);
    }

    /// <summary>Converts a marker at the beginning of a sentence to an expression.</summary>
    /// <remarks>
    /// Unknown bracketed text is not treated as a marker.
    /// </remarks>
    private static SpeechExpression ReadExpression(string sentence)
    {
        ExpressionMarkers.TryRead(sentence, out var expression, out _);

        return expression;
    }

    /// <summary>Determines the turn completion reason from an error code.</summary>
    private static TurnCompletionReason ReasonFor(GatewayError error) =>
        error.Code == GatewayErrorCode.Cancelled
            ? TurnCompletionReason.Cancelled
            : TurnCompletionReason.Failed;

    /// <summary>Runs an asynchronous operation and converts exceptions to safe errors.</summary>
    /// <remarks>The value is the default on failure. Callers must check the error before using it.</remarks>
    private async ValueTask<(T? Value, GatewayError? Error)> TryAsync<T>(
        Func<Task<T>> step,
        TurnScope scope)
    {
        try
        {
            return (await step().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (scope.Aborted.IsCancellationRequested)
        {
            return (default, TurnErrorMapper.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return (default, TurnErrorMapper.Timeout);
        }
        catch (ProviderException exception)
        {
            var error = TurnErrorMapper.FromProvider(exception);

            // Send internal error details to the diagnostic callback, not to the client.
            if (error.Code == GatewayErrorCode.Internal)
            {
                _onUnexpected?.Invoke(exception);
            }

            return (default, error);
        }
        catch (Exception exception)
        {
            // Replace unknown exceptions with a standard message and send details only to diagnostics.
            _onUnexpected?.Invoke(exception);

            return (default, TurnErrorMapper.Unexpected);
        }
    }

    /// <summary>Runs an asynchronous operation without a return value and converts exceptions to safe errors.</summary>
    private async ValueTask<GatewayError?> TryAsync(
        Func<Task> step,
        TurnScope scope)
    {
        var (_, error) = await TryAsync(
            async () =>
            {
                await step().ConfigureAwait(false);
                return true;
            },
            scope).ConfigureAwait(false);

        return error;
    }

    /// <summary>Disposes the concurrency gate and session locks.</summary>
    public void Dispose()
    {
        _gate.Dispose();
        _sessionGates.Dispose();
    }

}
