namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>Represents a speech-recognition result.</summary>
/// <param name="Text">Recognized text, or an empty string when no utterance was recognized.</param>
public sealed record Transcript(string Text);
