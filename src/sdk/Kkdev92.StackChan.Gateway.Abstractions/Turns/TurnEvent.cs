namespace Kkdev92.StackChan.Gateway.Abstractions.Turns;

/// <summary>
/// Represents an event produced while a turn is in progress.
/// </summary>
/// <remarks>
/// Events do not include transport details such as sequence numbers, Base64 encoding, or envelope names.
/// </remarks>
public abstract record TurnEvent;

/// <summary>Indicates that speech recognition completed.</summary>
/// <param name="Text">Recognized text.</param>
public sealed record TranscriptAvailable(
    string Text) : TurnEvent;

/// <summary>
/// Indicates that response text and audio were generated for one sentence.
/// </summary>
/// <remarks>
/// Text can still be sent when synthesis fails; in that case, <paramref name="Audio"/> is
/// <see cref="PcmAudio.Silence"/>. <paramref name="Text"/> excludes protocol-specific expression
/// labels such as <c>[happy]</c>.
/// </remarks>
/// <param name="Text">Text to speak, without expression labels.</param>
/// <param name="Expression">Expression shown while speaking.</param>
/// <param name="Audio">Speech audio, or <see cref="PcmAudio.Silence"/> when audio is unavailable.</param>
public sealed record ReplyAudioAvailable(
    string Text,
    SpeechExpression Expression,
    PcmAudio Audio) : TurnEvent;

/// <summary>Indicates that turn processing failed.</summary>
/// <param name="Error">Error information that can be reported to the device.</param>
public sealed record TurnFailed(
    GatewayError Error) : TurnEvent;

/// <summary>Indicates that a turn ended.</summary>
/// <param name="Reason">Reason the turn ended.</param>
public sealed record TurnCompleted(
    TurnCompletionReason Reason) : TurnEvent;

/// <summary>Identifies why a turn ended.</summary>
public enum TurnCompletionReason
{
    /// <summary>The complete response was sent.</summary>
    Completed,

    /// <summary>The turn ended because of an error.</summary>
    Failed,

    /// <summary>The turn ended because it was cancelled.</summary>
    Cancelled,
}
