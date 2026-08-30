namespace Kkdev92.StackChan.Gateway.Diagnostics;

/// <summary>
/// Represents settings for checking a device connection without external services.
/// </summary>
/// <remarks>
/// This diagnostic mode replaces speech recognition, the agent, and synthesis with fixed
/// implementations. It provides connection checks rather than conversational responses.
/// </remarks>
public sealed class OfflineOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "StackChan:Offline";

    /// <summary>Gets or sets whether the host selects offline diagnostics.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the fixed text returned as the recognition result.</summary>
    public string Transcript { get; set; } = "(fixed-response mode)";

    /// <summary>Gets or sets response sentences returned by the agent in order.</summary>
    /// <remarks>
    /// Each element may include an expression marker such as <c>[happy]</c>. The value cannot be
    /// <see langword="null"/>.
    /// </remarks>
    public string[] FixedResponse { get; set; } = [];

    /// <summary>Gets the defaults used when no response sentence is configured.</summary>
    /// <remarks>Returns a new array on each access.</remarks>
    public static string[] DefaultFixedResponse =>
    [
        "[happy]こんにちは、スタックちゃんです。",
        "[neutral]接続できました。",
    ];

    /// <summary>
    /// Applies default response sentences when <see cref="FixedResponse"/> is empty.
    /// </summary>
    /// <remarks>
    /// Call this after configuration binding. A default array in the property initializer would
    /// cause bound values to be appended and leave unintended responses in the array.
    /// </remarks>
    public void ApplyDefaults()
    {
        if (FixedResponse.Length == 0)
        {
            FixedResponse = DefaultFixedResponse;
        }
    }
}
