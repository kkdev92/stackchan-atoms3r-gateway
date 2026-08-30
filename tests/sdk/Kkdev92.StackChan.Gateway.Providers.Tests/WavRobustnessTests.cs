using System.Buffers.Binary;
using Kkdev92.StackChan.Gateway.Providers.Audio;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Providers.Tests;

/// <summary>Verifies safe handling of WAV data with malformed chunk lengths.</summary>
/// <remarks>
/// RIFF chunk lengths are <c>uint</c>; converting values over 2 GiB to <c>int</c> produces negative
/// numbers. Unknown chunks must not move the read position backward, data chunks must not overflow
/// array lengths, and processing must remain within the actual buffer.
/// </remarks>
public sealed class WavRobustnessTests
{
    /// <summary>Creates WAV data containing an unknown chunk.</summary>
    /// <param name="unknownSize">The length declared by the unknown chunk.</param>
    private static byte[] WithUnknownChunk(uint unknownSize)
    {
        var buffer = new byte[80];

        "RIFF"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 72);
        "WAVE"u8.CopyTo(buffer.AsSpan(8));

        "fmt "u8.CopyTo(buffer.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24), 16000);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), 32000);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(32), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(34), 16);

        // Exercise the path that skips an unknown chunk.
        "LIST"u8.CopyTo(buffer.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(40), unknownSize);

        return buffer;
    }

    /// <summary>Creates WAV data with a data chunk that declares a malformed length.</summary>
    private static byte[] WithBrokenData(uint dataSize)
    {
        var buffer = WithUnknownChunk(0);

        "data"u8.CopyTo(buffer.AsSpan(44));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(48), dataSize);

        return buffer;
    }

    [Theory]
    // A value that becomes negative when converted to int.
    [InlineData(0xFFFFFFF8u)]
    [InlineData(0x80000000u)]
    [InlineData(0xFFFFFFFFu)]
    // A positive value far larger than the remaining buffer.
    [InlineData(1_000_000u)]
    public async Task 未知のチャンク長が不正でも_処理を終了する(uint size)
    {
        var wav = WithUnknownChunk(size);

        // Reject an unreadable format with InvalidDataException without entering an infinite loop.
        var run = Task.Run(() =>
        {
            try
            {
                WavAudio.ToTargetPcm(wav);

                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        });

        (await FinishedAsync(run)).ShouldBeTrue("WAV チャンクの走査が 5 秒以内に完了しませんでした。");
        (await run).ShouldBeTrue("不正なチャンク長に対して InvalidDataException が発生しませんでした。");
    }

    [Theory]
    [InlineData(0xFFFFFFF8u)]
    [InlineData(0x80000000u)]
    [InlineData(0xFFFFFFFFu)]
    public async Task data_チャンク長が不正でも_バッファの外を読まない(uint size)
    {
        var wav = WithBrokenData(size);

        // Return samples present in the buffer even when the declared length is too large.
        var run = Task.Run(() => WavAudio.ToTargetPcm(wav));

        (await FinishedAsync(run)).ShouldBeTrue("WAV の解析が 5 秒以内に完了しませんでした。");

        var pcm = await run;

        // Read only samples remaining in the actual buffer even when the declaration says 4 GiB.
        pcm.Length.ShouldBeLessThanOrEqualTo(wav.Length / 2);
    }

    /// <summary>Returns whether processing completes within five seconds.</summary>
    private static async Task<bool> FinishedAsync(Task work) =>
        await Task.WhenAny(
            work,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            .ConfigureAwait(false) == work;

    [Fact]
    public void 正しい_WAV_は正常に読み取れる()
    {
        // Ensure malformed-input bounds do not affect ordinary WAV parsing.
        var buffer = WithUnknownChunk(0);

        "data"u8.CopyTo(buffer.AsSpan(44));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(48), 8);

        for (var index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                buffer.AsSpan(52 + (index * 2)), (short)(index * 100));
        }

        var pcm = WavAudio.ToTargetPcm(buffer);

        pcm.Length.ShouldBe(4);
        pcm[1].ShouldBe((short)100);
    }
}
