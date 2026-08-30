namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>
/// Defines size limits for events sent to an AtomS3R device.
/// </summary>
/// <remarks>
/// The device discards an entire event that exceeds a limit rather than accepting a partial event.
/// A discarded <c>reply.audio</c> event creates a sequence gap and causes the turn to fail.
/// </remarks>
public static class DeviceLimits
{
    /// <summary>
    /// Represents the maximum number of bytes allowed in one complete SSE line.
    /// </summary>
    /// <remarks>
    /// The limit applies to the complete UTF-8 encoded line, including the <c>data: </c> prefix.
    /// </remarks>
    public const int MaxEventBytes = 8192;

    /// <summary>Represents the byte length of the SSE <c>data: </c> prefix.</summary>
    public const int DataFieldPrefixBytes = 6;

    /// <summary>Represents the maximum number of PCM bytes allowed in one event.</summary>
    /// <remarks>This is 128 milliseconds of 16 kHz, 16-bit, mono audio.</remarks>
    public const int MaxPcmBytes = 4096;

    /// <summary>
    /// Represents the maximum number of UTF-8 bytes allowed in a <c>text</c> field.
    /// </summary>
    /// <remarks>
    /// If the limit is exceeded, the device cannot expand the JSON string and discards the event.
    /// </remarks>
    public const int MaxTextBytes = 512;
}
