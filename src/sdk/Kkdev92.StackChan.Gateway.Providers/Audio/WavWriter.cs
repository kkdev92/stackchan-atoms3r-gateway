using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Providers.Audio;

/// <summary>
/// Converts PCM audio to uncompressed WAV data.
/// </summary>
/// <remarks>
/// Use this to send <see cref="PcmAudio"/> to a recognition service that accepts WAV files.
/// </remarks>
public static class WavWriter
{
    private const int HeaderBytes = 44;

    /// <summary>Creates PCM data with a 44-byte RIFF/WAVE header.</summary>
    /// <param name="audio">Signed 16-bit PCM audio to convert.</param>
    /// <returns>The complete WAV file as bytes.</returns>
    public static byte[] Build(PcmAudio audio)
    {
        var pcm = MemoryMarshal.AsBytes(audio.Samples.Span);
        var wav = new byte[HeaderBytes + pcm.Length];
        var span = wav.AsSpan();

        var byteRate = audio.SampleRate * audio.Channels * 2;
        var blockAlign = (ushort)(audio.Channels * 2);

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(36 + pcm.Length));
        "WAVE"u8.CopyTo(span[8..]);

        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], (ushort)audio.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)audio.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);

        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)pcm.Length);
        pcm.CopyTo(span[HeaderBytes..]);

        return wav;
    }
}
