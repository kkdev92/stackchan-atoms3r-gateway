namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R;

/// <summary>
/// Represents AtomS3R protocol settings.
/// </summary>
/// <remarks>
/// The default keep-alive interval is well below the device timeout for an idle connection.
/// </remarks>
public sealed class Atoms3ROptions
{
    /// <summary>Gets the section name used in configuration.</summary>
    public const string SectionName = "StackChan:Atoms3R";

    /// <summary>
    /// Gets or sets the shared token used to authenticate the device.
    /// </summary>
    /// <remarks>
    /// An empty value disables token authentication. Supply the token through a protected
    /// configuration source such as <c>StackChan__Atoms3R__Token</c>, not a tracked settings file.
    /// </remarks>
    public string Token { get; set; } = "";

    /// <summary>Gets or sets the maximum byte length of an incoming WAV request body.</summary>
    public long MaxRequestBodyBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum UTF-8 byte length allowed in a text conversation request.
    /// </summary>
    /// <remarks>
    /// This limit is applied separately from <see cref="MaxRequestBodyBytes"/>. Incoming text also
    /// becomes agent input and conversation history, so the response text limit alone cannot bound it.
    /// </remarks>
    public int MaxSpokenTextBytes { get; set; } = 4096;

    /// <summary>Gets or sets the SSE keep-alive interval, in seconds, while no response is available.</summary>
    public int KeepAliveIntervalSeconds { get; set; } = 3;
}
