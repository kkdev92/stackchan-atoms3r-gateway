using System.Buffers.Binary;
using System.Text;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Providers.Audio;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Providers.Tests;

/// <summary>
/// Verifies conversion between WAV and device-ready PCM audio.
/// </summary>
/// <remarks>
/// The device accepts 16 kHz, s16le, mono audio.
/// </remarks>
public sealed class WavAudioTests
{
    [Fact]
    public void 入力がデバイス形式と同じなら_サンプルをそのまま返す()
    {
        var samples = new short[] { 1, -1, 32767, -32768 };

        // WavAudio returns PCM samples; the provider constructs PcmAudio.
        var pcm = WavAudio.ToTargetPcm(Wav(16000, 1, samples));

        pcm.ShouldBe(samples);

        // Format metadata supplied by the provider also matches the device contract.
        new PcmAudio(pcm, PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels)
            .IsCanonical.ShouldBeTrue();
    }

    [Fact]
    public void 高いサンプルレートは_16kHz_へ変換する()
    {
        // Convert a 22.05 kHz voice model to the device's 16 kHz format.
        var pcm = WavAudio.ToTargetPcm(Wav(22050, 1, new short[2205]));

        pcm.Length.ShouldBe(2205 * 16000 / 22050);
    }

    [Fact]
    public void ステレオ音声は_モノラルへダウンミックスする()
    {
        // Use distinct left and right samples to verify each frame is averaged.
        var frames = new short[] { 100, 300, -100, -300 };

        var pcm = WavAudio.ToTargetPcm(Wav(16000, 2, frames));

        pcm.Length.ShouldBe(2);
        pcm[0].ShouldBe((short)200);
        pcm[1].ShouldBe((short)-200);
    }

    [Fact]
    public void fmt_と_data_の間に未知のチャンクがあっても読み取れる()
    {
        // Support WAV data with a LIST chunk without assuming a fixed 44-byte header.
        WavAudio.ToTargetPcm(WavWithList(16000, 1, new short[64])).Length.ShouldBe(64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a wav at all")]
    public void WAV_形式でなければ_InvalidDataException_を投げる(string text)
    {
        Should.Throw<InvalidDataException>(
            () => WavAudio.ToTargetPcm(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void 長さ_0_の_data_チャンクは_空の音声として返す()
    {
        // A zero-length WAV with a data chunk is valid; the runtime handles it as synthesis failure.
        // This allows generated text to still reach the device.
        WavAudio.ToTargetPcm(Wav(16000, 1, [])).ShouldBeEmpty();
    }

    [Fact]
    public void data_チャンクが無ければ_InvalidDataException_を投げる()
    {
        // Report missing required chunks to the caller as malformed data.
        var headerOnly = Wav(16000, 1, [])[..36];

        Should.Throw<InvalidDataException>(() => WavAudio.ToTargetPcm(headerOnly));
    }

    [Fact]
    public void 書き出した_WAV_を読み直すと_元のサンプルへ戻る()
    {
        // Verify WavWriter and WavAudio formats agree through a round trip.
        var samples = new short[320];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(i * 37 % 3000 - 1500);
        }

        var original = new PcmAudio(samples, 16000, 1);

        WavAudio.ToTargetPcm(WavWriter.Build(original)).ShouldBe(samples);
    }

    [Fact]
    public void 書き出した_WAV_は_44_バイトの標準ヘッダーを持つ()
    {
        // Preserve compatibility with endpoints that expect a fixed-length header.
        var wav = WavWriter.Build(new PcmAudio(new short[160], 16000, 1));

        wav.Length.ShouldBe(44 + 320);
        Encoding.ASCII.GetString(wav, 0, 4).ShouldBe("RIFF");
        Encoding.ASCII.GetString(wav, 8, 4).ShouldBe("WAVE");
        Encoding.ASCII.GetString(wav, 12, 4).ShouldBe("fmt ");
        Encoding.ASCII.GetString(wav, 36, 4).ShouldBe("data");
        BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(20, 2)).ShouldBe((ushort)1);
        BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(22, 2)).ShouldBe((ushort)1);
        BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(24, 4)).ShouldBe(16000u);
        BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(34, 2)).ShouldBe((ushort)16);
        BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(40, 4)).ShouldBe(320u);
    }

    private static byte[] Wav(int sampleRate, int channels, short[] samples) =>
        Riff(sampleRate, channels, samples, includeList: false);

    private static byte[] WavWithList(int sampleRate, int channels, short[] samples) =>
        Riff(sampleRate, channels, samples, includeList: true);

    private static byte[] Riff(int sampleRate, int channels, short[] samples, bool includeList)
    {
        var pcm = new byte[samples.Length * 2];

        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), samples[i]);
        }

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII);

        var listSize = includeList ? 12 : 0;

        writer.Write("RIFF"u8);
        writer.Write(36 + listSize + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);

        if (includeList)
        {
            writer.Write("LIST"u8);
            writer.Write(4);
            writer.Write("INFO"u8);
        }

        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        writer.Flush();

        return buffer.ToArray();
    }
}
