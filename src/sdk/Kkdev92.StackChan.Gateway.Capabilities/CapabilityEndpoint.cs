namespace Kkdev92.StackChan.Gateway.Capabilities;

/// <summary>
/// Validates external service endpoints used by capabilities.
/// </summary>
public static class CapabilityEndpoint
{
    /// <summary>Determines whether a string is an absolute HTTP or HTTPS URI.</summary>
    /// <param name="endpoint">Endpoint to validate; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value is a valid absolute HTTP or HTTPS URI.</returns>
    public static bool IsAbsoluteHttp(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
