namespace Kkdev92.StackChan.Gateway.TestKit;

/// <summary>
/// Creates WAV data for tests.
/// </summary>
/// <remarks>
/// The returned byte array can be used directly in an HTTP request and does not depend on SDK-specific audio types.
/// </remarks>
public static class WavFactory
{
    /// <summary>Returns a silent 16 kHz, mono, 16-bit little-endian WAV stream.</summary>
    public static MemoryStream Speech(int samples = 1600)
    {
        var body = new byte[samples * 2];
        return new MemoryStream(Wav(body, 16000, 1));
    }

    /// <summary>Adds a 44-byte WAV header to PCM data.</summary>
    public static byte[] Wav(byte[] pcm, int sampleRate, int channels)
    {
        var wav = new byte[44 + pcm.Length];
        var at = 0;

        void Ascii(string text)
        {
            foreach (var c in text)
            {
                wav[at++] = (byte)c;
            }
        }

        void U32(uint value)
        {
            BitConverter.TryWriteBytes(wav.AsSpan(at, 4), value);
            at += 4;
        }

        void U16(ushort value)
        {
            BitConverter.TryWriteBytes(wav.AsSpan(at, 2), value);
            at += 2;
        }

        Ascii("RIFF");
        U32((uint)(36 + pcm.Length));
        Ascii("WAVE");
        Ascii("fmt ");
        U32(16);
        U16(1);
        U16((ushort)channels);
        U32((uint)sampleRate);
        U32((uint)(sampleRate * channels * 2));
        U16((ushort)(channels * 2));
        U16(16);
        Ascii("data");
        U32((uint)pcm.Length);
        pcm.CopyTo(wav, 44);

        return wav;
    }
}
