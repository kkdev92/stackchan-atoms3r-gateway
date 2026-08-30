using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R;

/// <summary>
/// Converts WAV data received from AtomS3R to <see cref="PcmAudio"/>.
/// </summary>
/// <remarks>
/// Only a standard 44-byte header and 16 kHz mono signed 16-bit little-endian PCM are accepted.
/// Other WAV formats are rejected without conversion.
/// </remarks>
internal static class DeviceWav
{
    /// <summary>Accepted WAV header length in bytes.</summary>
    public const int HeaderBytes = 44;

    /// <summary>
    /// Parses a request body as PCM audio.
    /// </summary>
    /// <param name="body">Request body.</param>
    /// <param name="audio">Parsed audio.</param>
    /// <param name="error">Client-safe error message when parsing fails.</param>
    public static bool TryRead(ReadOnlySpan<byte> body, out PcmAudio audio, out string? error)
    {
        audio = PcmAudio.Silence;

        if (body.Length < HeaderBytes)
        {
            error = "wav is required";
            return false;
        }

        if (!body[..4].SequenceEqual("RIFF"u8) || !body[8..12].SequenceEqual("WAVE"u8))
        {
            error = "wav is required";
            return false;
        }

        // Accept only the fixed fmt and data layout produced by standard firmware.
        if (!body[12..16].SequenceEqual("fmt "u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[16..20]) != 16 ||
            !body[36..40].SequenceEqual("data"u8))
        {
            error = "unexpected wav layout";
            return false;
        }

        var format = BinaryPrimitives.ReadUInt16LittleEndian(body[20..22]);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(body[22..24]);
        var rate = BinaryPrimitives.ReadUInt32LittleEndian(body[24..28]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(body[34..36]);

        if (format != 1 ||
            bits != 16 ||
            channels != PcmAudio.CanonicalChannels ||
            rate != PcmAudio.CanonicalSampleRate)
        {
            error = "unsupported wav format";
            return false;
        }

        // Bound the declared length by bytes received so a truncated body cannot expose uninitialized data.
        var declared = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[40..44]);
        var available = body.Length - HeaderBytes;
        var length = Math.Min(declared, available);

        if (length <= 0)
        {
            error = "wav is required";
            return false;
        }

        var pcm = body.Slice(HeaderBytes, length - (length % 2));
        var samples = new short[pcm.Length / 2];
        pcm.CopyTo(MemoryMarshal.AsBytes(samples.AsSpan()));

        audio = new PcmAudio(samples, PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);
        error = null;

        return true;
    }
}
