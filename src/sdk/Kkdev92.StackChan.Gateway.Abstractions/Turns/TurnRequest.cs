namespace Kkdev92.StackChan.Gateway.Abstractions.Turns;

/// <summary>
/// Represents one turn request received from a device.
/// </summary>
/// <remarks>
/// Audio input is stored as normalized <see cref="PcmAudio"/> rather than a file format such as WAV.
/// For text input, <see cref="UserText"/> is populated and the turn skips speech recognition. Use
/// <see cref="FromAudio"/> or <see cref="FromText"/> so exactly one input form is set.
/// </remarks>
public sealed record TurnRequest
{
    private TurnRequest(
        SessionId sessionId,
        DeviceTurnContext device,
        PcmAudio audio,
        string? userText)
    {
        // Value types can be default-constructed without invoking their constructors, so validate at the boundary.
        if (!sessionId.IsSet)
        {
            throw new ArgumentException(
                "Session id must not be default or empty.", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(device);

        SessionId = sessionId;
        Device = device;
        Audio = audio;
        UserText = userText;
    }

    /// <summary>Gets the session identifier used to associate conversation history.</summary>
    public SessionId SessionId { get; }

    /// <summary>Gets device information that identifies the source of the turn.</summary>
    public DeviceTurnContext Device { get; }

    /// <summary>Gets audio for one utterance.</summary>
    /// <remarks>This is <see cref="PcmAudio.Silence"/> for text input.</remarks>
    public PcmAudio Audio { get; }

    /// <summary>Gets the text input.</summary>
    /// <remarks>This is <see langword="null"/> for audio input.</remarks>
    public string? UserText { get; }

    /// <summary>Creates a turn request from one utterance of audio.</summary>
    /// <param name="sessionId">Session identifier used to associate conversation history.</param>
    /// <param name="device">Device information that identifies the source of the turn.</param>
    /// <param name="audio">Audio for one utterance.</param>
    /// <returns>A turn request containing audio input.</returns>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is not set.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="device"/> or <paramref name="audio"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method does not validate <see cref="PcmAudio.IsCanonical"/>. Audio in another format is
    /// handled as an error when the turn executes.
    /// </remarks>
    public static TurnRequest FromAudio(
        SessionId sessionId,
        DeviceTurnContext device,
        PcmAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);

        return new TurnRequest(sessionId, device, audio, userText: null);
    }

    /// <summary>Creates a turn request from text input.</summary>
    /// <param name="sessionId">Session identifier used to associate conversation history.</param>
    /// <param name="device">Device information that identifies the source of the turn.</param>
    /// <param name="userText">Text entered by the user.</param>
    /// <returns>A turn request containing text input.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="sessionId"/> is not set, or <paramref name="userText"/> is
    /// <see langword="null"/>, empty, or consists only of white space.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public static TurnRequest FromText(
        SessionId sessionId,
        DeviceTurnContext device,
        string userText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userText);

        return new TurnRequest(sessionId, device, PcmAudio.Silence, userText);
    }
}
