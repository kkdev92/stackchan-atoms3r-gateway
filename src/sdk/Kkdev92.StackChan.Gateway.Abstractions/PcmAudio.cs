namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>
/// Represents signed 16-bit PCM audio.
/// </summary>
/// <remarks>
/// Audio sent to the device must be converted to 16 kHz mono so <see cref="IsCanonical"/> is
/// <see langword="true"/>. The sample memory is not copied; do not modify the underlying data while
/// this instance is in use.
/// </remarks>
/// <param name="Samples">Signed 16-bit PCM samples.</param>
/// <param name="SampleRate">Sample rate in hertz.</param>
/// <param name="Channels">Number of channels.</param>
public sealed record PcmAudio(
    ReadOnlyMemory<short> Samples,
    int SampleRate,
    int Channels)
{
    /// <summary>Represents the sample rate accepted by the device, in hertz.</summary>
    public const int CanonicalSampleRate = 16000;

    /// <summary>Represents the channel count accepted by the device.</summary>
    public const int CanonicalChannels = 1;

    /// <summary>Gets canonical audio with no samples.</summary>
    /// <remarks>Use this when synthesis fails and only text can be returned.</remarks>
    public static PcmAudio Silence { get; } =
        new(ReadOnlyMemory<short>.Empty, CanonicalSampleRate, CanonicalChannels);

    /// <summary>Gets whether the sample rate and channel count match the device format.</summary>
    public bool IsCanonical =>
        SampleRate == CanonicalSampleRate && Channels == CanonicalChannels;
}
