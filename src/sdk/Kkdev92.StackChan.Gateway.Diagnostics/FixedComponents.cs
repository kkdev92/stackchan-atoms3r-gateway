using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Diagnostics;

/// <summary>
/// Returns the configured transcript without analyzing the input audio.
/// </summary>
/// <remarks>
/// This implementation checks the device-to-gateway connection without a speech-recognition service.
/// </remarks>
/// <param name="options">Offline diagnostic settings containing the transcript to return.</param>
public sealed class FixedTranscriptSpeechToText(OfflineOptions options) : ISpeechToText
{
    /// <inheritdoc />
    public Task<Transcript> TranscribeAsync(PcmAudio audio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new Transcript(options.Transcript));
    }
}

/// <summary>
/// Returns the configured response sentences in order.
/// </summary>
/// <remarks>
/// Responses follow the normal agent-output path, allowing expression-marker parsing and device
/// event generation to be checked.
/// </remarks>
/// <param name="options">Offline diagnostic settings containing the responses to return.</param>
public sealed class FixedResponseAgent(OfflineOptions options) : IAgent
{
    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        AgentRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var sentence in options.FixedResponse)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return sentence;
        }
    }
}

/// <summary>
/// Generates a short confirmation tone at a different frequency for each input sentence.
/// </summary>
/// <remarks>
/// This implementation checks playback through the device speaker without a synthesis service.
/// Consecutive sentences receive different frequencies; text content does not affect the tone.
/// </remarks>
public sealed class ToneTextToSpeech : ITextToSpeech
{
    // Cycle through frequencies so sentence boundaries are audible.
    private static readonly double[] Notes = [523.25, 440.0, 659.25, 349.23, 587.33];

    private int _sentenceIndex = -1;

    /// <inheritdoc />
    public Task<PcmAudio> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var index = Interlocked.Increment(ref _sentenceIndex);

        return Task.FromResult(new PcmAudio(
            Tone(index),
            PcmAudio.CanonicalSampleRate,
            PcmAudio.CanonicalChannels));
    }

    private static short[] Tone(int index)
    {
        const int rate = PcmAudio.CanonicalSampleRate;
        const double seconds = 1.0;

        var hz = Notes[index % Notes.Length];
        var count = (int)(rate * seconds);
        var samples = new short[count];

        for (var i = 0; i < count; i++)
        {
            // Fade the waveform edges to suppress clicks from abrupt amplitude changes.
            var envelope = Math.Min(
                1.0,
                Math.Min(i / (rate * 0.01), (count - 1 - i) / (rate * 0.01)));

            samples[i] = (short)(9000 * envelope * Math.Sin(2 * Math.PI * hz * i / rate));
        }

        return samples;
    }
}
