using System.Text;
using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R;

/// <summary>
/// Adjusts text length for delivery to AtomS3R.
/// </summary>
/// <remarks>
/// The device discards an entire event that exceeds its limit. Discarding <c>reply.audio</c> creates
/// a sequence gap and fails the conversation, so text is truncated before transmission.
/// </remarks>
internal static class DeviceText
{
    /// <summary>Maximum UTF-8 byte length allowed in a <c>text</c> field.</summary>
    public const int MaxBytes = DeviceLimits.MaxTextBytes;

    /// <summary>
    /// Truncates text to a length the device can receive.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <remarks>
    /// Truncation does not split a Unicode scalar value. This limit applies only to device responses
    /// and does not alter agent input.
    /// </remarks>
    public static string Clamp(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (Encoding.UTF8.GetByteCount(text) <= MaxBytes)
        {
            return text;
        }

        var bytes = 0;
        var end = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;

            if (bytes + size > MaxBytes)
            {
                break;
            }

            bytes += size;
            end += rune.Utf16SequenceLength;
        }

        return text[..end];
    }
}
