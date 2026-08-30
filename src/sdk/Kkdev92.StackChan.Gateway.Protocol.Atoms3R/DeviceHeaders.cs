using System.Buffers;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R;

/// <summary>Validates AtomS3R identity headers.</summary>
/// <remarks>
/// Standard firmware generates values in these forms:
/// <list type="bullet">
///   <item><description><c>X-StackChan-Device</c>: 20 characters consisting of <c>atoms3r-</c> and a MAC address.</description></item>
///   <item><description><c>X-StackChan-Boot</c>: a 128-bit value encoded as 26 Crockford Base32 characters.</description></item>
///   <item><description><c>X-StackChan-Conversation</c>: up to 47 characters in the form <c>&lt;boot&gt;-&lt;sequence&gt;</c>.</description></item>
/// </list>
/// <para>
/// Validation does not require these exact formats so custom implementations remain possible.
/// Values are length- and character-limited because they become session keys and log fields.
/// Malformed values are rejected rather than truncated, which could collide with another identifier.
/// </para>
/// </remarks>
internal static class DeviceHeaders
{
    /// <summary>Maximum characters allowed in an identifier.</summary>
    public const int MaxIdLength = 128;

    /// <summary>ASCII characters allowed in an identifier.</summary>
    /// <remarks>
    /// In addition to the alphanumeric characters and hyphen used by standard firmware, custom
    /// implementations may use <c>_</c>, <c>.</c>, and <c>:</c>. Control characters, white space,
    /// quotation marks, and non-ASCII characters are excluded to prevent log injection and
    /// confusable identifiers.
    /// </remarks>
    private static readonly SearchValues<char> Allowed = SearchValues.Create(
        "0123456789" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "abcdefghijklmnopqrstuvwxyz" +
        "-_.:");

    /// <summary>Returns whether the value is a valid non-empty identifier.</summary>
    public static bool IsWellFormed(string value) =>
        value.Length is > 0 and <= MaxIdLength &&
        !value.AsSpan().ContainsAnyExcept(Allowed);

    /// <summary>Returns whether the value is a valid optional boot or conversation identifier.</summary>
    public static bool IsWellFormedOrEmpty(string value) =>
        value.Length == 0 || IsWellFormed(value);
}
