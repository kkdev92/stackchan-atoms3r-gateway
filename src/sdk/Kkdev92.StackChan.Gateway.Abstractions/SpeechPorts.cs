namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>
/// Converts speech audio to text.
/// </summary>
/// <remarks>
/// An implementation that requires a file format such as WAV converts the supplied
/// <see cref="PcmAudio"/> internally.
/// </remarks>
public interface ISpeechToText
{
    /// <summary>Recognizes one utterance.</summary>
    /// <param name="audio">Audio for which <see cref="PcmAudio.IsCanonical"/> is <see langword="true"/>.</param>
    /// <param name="cancellationToken">Token that signals cancellation of recognition.</param>
    /// <returns>Recognized text, or an empty string when no utterance was recognized.</returns>
    Task<Transcript> TranscribeAsync(
        PcmAudio audio,
        CancellationToken cancellationToken);
}

/// <summary>
/// Converts text to speech audio.
/// </summary>
/// <remarks>
/// Implementations return audio for which <see cref="PcmAudio.IsCanonical"/> is <see langword="true"/>.
/// </remarks>
public interface ITextToSpeech
{
    /// <summary>Generates speech audio for one sentence.</summary>
    /// <param name="text">Text to speak, without expression markers.</param>
    /// <param name="cancellationToken">Token that signals cancellation of synthesis.</param>
    /// <returns>PCM audio converted to the device format.</returns>
    Task<PcmAudio> SynthesizeAsync(
        string text,
        CancellationToken cancellationToken);
}
