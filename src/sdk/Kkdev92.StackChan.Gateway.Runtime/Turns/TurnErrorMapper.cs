using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Runtime.Turns;

/// <summary>
/// Converts exceptions to <see cref="GatewayError"/> values that can be returned to clients.
/// </summary>
/// <remarks>
/// The returned errors contain only a client-actionable code and safe message. Internal details
/// such as endpoints, tokens, and stack traces are not exposed.
/// </remarks>
internal static class TurnErrorMapper
{
    /// <summary>The error returned when the concurrency limit is reached.</summary>
    public static GatewayError Busy { get; } =
        new(GatewayErrorCode.Busy, "busy", Retryable: true);

    /// <summary>The error returned when processing is cancelled.</summary>
    public static GatewayError Cancelled { get; } =
        new(GatewayErrorCode.Cancelled, "cancelled", Retryable: false);

    /// <summary>The error returned when speech recognition fails.</summary>
    public static GatewayError SpeechRecognitionFailed { get; } =
        new(GatewayErrorCode.Unavailable, "speech recognition failed", Retryable: true);

    /// <summary>The error returned when the agent produces no response.</summary>
    public static GatewayError NoReply { get; } =
        new(GatewayErrorCode.Unavailable, "the model produced no reply", Retryable: true);

    /// <summary>
    /// The error returned when speech synthesis fails for every sentence.
    /// </summary>
    /// <remarks>
    /// If only some sentences fail, the runtime sends their text and continues. If every sentence
    /// fails, the turn fails because the speech provider is unavailable.
    /// </remarks>
    public static GatewayError NoVoice { get; } =
        new(GatewayErrorCode.Unavailable, "the voice provider could not speak", Retryable: true);

    /// <summary>Converts an error returned by a provider.</summary>
    public static GatewayError FromProvider(ProviderException exception) =>
        new(exception.Code, exception.Message, exception.Retryable);

    /// <summary>The error returned when an operation times out.</summary>
    public static GatewayError Timeout { get; } =
        new(GatewayErrorCode.Timeout, "the provider did not answer in time", Retryable: true);

    /// <summary>The error returned for an exception that cannot be mapped more specifically.</summary>
    public static GatewayError Unexpected { get; } =
        new(GatewayErrorCode.Internal, "unexpected gateway error", Retryable: false);
}
