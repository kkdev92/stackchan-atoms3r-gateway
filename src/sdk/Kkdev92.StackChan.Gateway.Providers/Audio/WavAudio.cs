using System.Buffers.Binary;
using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Providers.Audio;

/// <summary>
/// Converts PCM WAV audio to 16 kHz mono audio for the device.
/// </summary>
/// <remarks>
/// Multi-channel input is mixed down to mono, and differing sample rates are converted with linear
/// interpolation. Input must be uncompressed signed 16-bit PCM.
/// </remarks>
public static class WavAudio
{
    /// <summary>Represents the output sample rate in hertz.</summary>
    public const int TargetRate = PcmAudio.CanonicalSampleRate;

    /// <summary>
    /// Reads WAV data and converts it to 16 kHz mono PCM samples.
    /// </summary>
    /// <param name="wav">Audio data stored in a RIFF/WAVE container.</param>
    /// <returns>A new array containing signed 16-bit, 16 kHz mono PCM samples.</returns>
    /// <exception cref="InvalidDataException">
    /// Input is not RIFF/WAVE, lacks a required chunk, or is not signed 16-bit PCM.
    /// </exception>
    public static short[] ToTargetPcm(ReadOnlySpan<byte> wav)
    {
        var (samples, rate, channels) = Parse(wav);
        var mono = channels == 1 ? samples : MixDown(samples, channels);
        return rate == TargetRate ? mono : Resample(mono, rate, TargetRate);
    }

    /// <summary>
    /// Scans RIFF chunks and reads <c>fmt </c> and <c>data</c>.
    /// </summary>
    /// <remarks>
    /// Optional chunks such as <c>LIST</c> may appear between <c>fmt </c> and <c>data</c>, so the
    /// parser does not assume a fixed 44-byte header.
    /// </remarks>
    private static (short[] Samples, int Rate, int Channels) Parse(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 12 ||
            !wav[..4].SequenceEqual("RIFF"u8) ||
            !wav[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("The input is not in RIFF/WAVE format.");
        }

        var rate = 0;
        var channels = 0;
        var bits = 0;
        short[]? samples = null;

        var at = 12;
        while (at + 8 <= wav.Length)
        {
            var id = wav.Slice(at, 4);
            var body = at + 8;

            // Chunk lengths are uint values. Clamp to the remaining data before converting to int;
            // converting first could make values above 2 GiB negative and move the cursor backward.
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(at + 4, 4));
            var size = (int)Math.Min(declared, (uint)(wav.Length - body));

            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                var format = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 2, 2));
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.Slice(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.Slice(body + 14, 2));

                if (format != 1 || bits != 16)
                {
                    throw new InvalidDataException(
                        $"The input is not signed 16-bit PCM (format={format}, bits={bits}).");
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                var count = size / 2;
                samples = new short[count];
                for (var i = 0; i < count; i++)
                {
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.Slice(body + i * 2, 2));
                }
            }

            // RIFF chunks are word-aligned. Always advance the cursor, even for malformed input.
            var next = body + size + (size % 2);

            at = next > at ? next : at + 8;
        }

        if (samples is null || rate == 0 || channels == 0)
        {
            throw new InvalidDataException("The fmt or data chunk is missing.");
        }

        return (samples, rate, channels);
    }

    private static short[] MixDown(short[] interleaved, int channels)
    {
        var frames = interleaved.Length / channels;
        var mono = new short[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += interleaved[i * channels + c];
            }
            mono[i] = (short)(sum / channels);
        }
        return mono;
    }

    /// <summary>
    /// Converts the sample rate with linear interpolation.
    /// </summary>
    /// <remarks>
    /// This simple conversion does not apply a low-pass filter and is intended for conversational
    /// audio. Use a dedicated resampler for music or high-fidelity playback.
    /// </remarks>
    private static short[] Resample(short[] source, int fromRate, int toRate)
    {
        var outCount = (int)((long)source.Length * toRate / fromRate);
        var output = new short[outCount];
        var step = (double)fromRate / toRate;

        for (var i = 0; i < outCount; i++)
        {
            var position = i * step;
            var index = (int)position;
            var fraction = position - index;

            var a = source[Math.Min(index, source.Length - 1)];
            var b = source[Math.Min(index + 1, source.Length - 1)];
            output[i] = (short)(a + (b - a) * fraction);
        }

        return output;
    }
}
