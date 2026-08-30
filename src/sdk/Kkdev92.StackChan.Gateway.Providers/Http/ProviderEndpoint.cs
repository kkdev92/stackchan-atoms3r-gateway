using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Providers.Http;

/// <summary>Validates provider endpoints and classifies errors.</summary>
/// <remarks>
/// Each provider converts transport and response-parsing failures to <see cref="ProviderException"/>
/// with a message that is safe to disclose to the device.
/// </remarks>
public static class ProviderEndpoint
{
    /// <summary>Determines whether a string is an absolute HTTP or HTTPS URI.</summary>
    /// <param name="endpoint">Endpoint to validate; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value is a valid absolute HTTP or HTTPS URI.</returns>
    public static bool IsAbsoluteHttp(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Creates an exception indicating that a provider is unavailable.</summary>
    /// <remarks>
    /// <paramref name="message"/> may be sent to the device and must not contain endpoints,
    /// credentials, or other sensitive information.
    /// </remarks>
    /// <param name="message">Error message that is safe to disclose to the device.</param>
    /// <param name="inner">Underlying exception, or <see langword="null"/> when absent.</param>
    /// <param name="retryable"><see langword="true"/> when retrying the same request may succeed.</param>
    /// <returns>An exception classified as <see cref="GatewayErrorCode.Unavailable"/>.</returns>
    public static ProviderException Unavailable(
        string message,
        Exception? inner = null,
        bool retryable = true) =>
        new(GatewayErrorCode.Unavailable, message, retryable, inner);

    /// <summary>Determines whether an HTTP status code represents a retryable error.</summary>
    /// <remarks>
    /// Most 4xx responses are not retryable. 408 Request Timeout and 429 Too Many Requests are
    /// treated as temporary conditions.
    /// </remarks>
    /// <param name="status">HTTP status code.</param>
    /// <returns><see langword="true"/> when retrying may succeed.</returns>
    public static bool IsRetryableStatus(int status) =>
        status is not (>= 400 and < 500) || status is 408 or 429;

    /// <summary>Creates an exception representing a provider timeout.</summary>
    /// <remarks>
    /// Caller cancellation should propagate as <see cref="OperationCanceledException"/> rather than
    /// being converted. <paramref name="message"/> must not contain endpoints, credentials, or other
    /// sensitive information.
    /// </remarks>
    /// <param name="message">Error message that is safe to disclose to the device.</param>
    /// <returns>A retryable exception classified as <see cref="GatewayErrorCode.Timeout"/>.</returns>
    public static ProviderException Timeout(string message) =>
        new(GatewayErrorCode.Timeout, message, retryable: true);
}
