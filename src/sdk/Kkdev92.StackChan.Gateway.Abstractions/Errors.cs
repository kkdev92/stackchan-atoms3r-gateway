namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>
/// Identifies an error reported to the device.
/// </summary>
/// <remarks>
/// The device selects error handling from this value rather than from the error message.
/// </remarks>
public enum GatewayErrorCode
{
    /// <summary>A required service is unavailable.</summary>
    Unavailable,

    /// <summary>Processing did not complete before its deadline.</summary>
    Timeout,

    /// <summary>The request cannot be processed because of a limit such as concurrency.</summary>
    Busy,

    /// <summary>The request was cancelled.</summary>
    Cancelled,

    /// <summary>An unexpected internal error occurred.</summary>
    Internal,
}

/// <summary>
/// Represents error information that can be reported to the device.
/// </summary>
/// <remarks>
/// <paramref name="SafeMessage"/> may be written to device logs. It must not contain endpoints,
/// credentials, stack traces, or other sensitive information.
/// </remarks>
/// <param name="Code">Error category.</param>
/// <param name="SafeMessage">Error message that is safe to disclose to the device.</param>
/// <param name="Retryable"><see langword="true"/> when retrying the same request may succeed.</param>
public sealed record GatewayError(
    GatewayErrorCode Code,
    string SafeMessage,
    bool Retryable);

/// <summary>
/// Represents a provider operation failure.
/// </summary>
/// <remarks>
/// Store the device-safe message in <see cref="Exception.Message"/> and diagnostic details in
/// <see cref="Exception.InnerException"/>.
/// </remarks>
public sealed class ProviderException : Exception
{
    /// <summary>Creates an exception with an error category and device-safe message.</summary>
    /// <param name="code">Error category.</param>
    /// <param name="message">Error message that is safe to disclose to the device.</param>
    /// <param name="retryable"><see langword="true"/> when retrying the same request may succeed.</param>
    /// <param name="innerException">Underlying exception, or <see langword="null"/> when absent.</param>
    public ProviderException(
        GatewayErrorCode code,
        string message,
        bool retryable,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    /// <summary>Gets the error category.</summary>
    public GatewayErrorCode Code { get; }

    /// <summary>Gets whether retrying the same request may succeed.</summary>
    public bool Retryable { get; }
}
