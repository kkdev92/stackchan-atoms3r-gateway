using Microsoft.Extensions.Http.Logging;
using Microsoft.Extensions.Options;

namespace StackChan.Capability.Weather;

/// <summary>Logs HTTP request information without query strings.</summary>
/// <remarks>
/// <para>
/// The WeatherAPI.com API key appears in the URL query. Because URI masking in the standard HTTP
/// logger can be disabled at runtime, this logger always excludes the entire query. It retains the
/// HTTP method, host, path, status code, and elapsed time for diagnostics.
/// </para>
/// <para>
/// In case another HTTP handler includes a URL in an exception message, both the raw API key and its
/// URL-encoded form are also masked.
/// </para>
/// </remarks>
internal sealed class QueryFreeHttpLogger(
    ILogger<QueryFreeHttpLogger> logger,
    IOptions<WeatherOptions> options) : IHttpClientLogger
{
    private readonly string[] secrets = Secrets(options.Value.ApiKey);
    /// <inheritdoc />
    public object? LogRequestStart(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation(
            "http stage={Stage} method={Method} target={Target}",
            "start",
            request.Method.Method,
            Target(request.RequestUri));

        return null;
    }

    /// <inheritdoc />
    public void LogRequestStop(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage response,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);

        logger.LogInformation(
            "http stage={Stage} method={Method} target={Target} status={Status} duration_ms={Duration}",
            "stop",
            request.Method.Method,
            Target(request.RequestUri),
            (int)response.StatusCode,
            (long)elapsed.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void LogRequestFailed(
        object? context,
        HttpRequestMessage request,
        HttpResponseMessage? response,
        Exception exception,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentNullException.ThrowIfNull(exception);

        // Log a scrubbed string because passing the exception object would let the logger expand it again.
        logger.LogWarning(
            "http stage={Stage} method={Method} target={Target} duration_ms={Duration} error={Error}",
            "failed",
            request.Method.Method,
            Target(request.RequestUri),
            (long)elapsed.TotalMilliseconds,
            Scrub(exception.ToString()));
    }

    private string Scrub(string text)
    {
        foreach (var secret in secrets)
        {
            text = text.Replace(secret, "***", StringComparison.Ordinal);
        }

        return text;
    }

    // Also remove the URL-encoded representation from exception messages.
    private static string[] Secrets(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return [];
        }

        var escaped = Uri.EscapeDataString(apiKey);

        return string.Equals(escaped, apiKey, StringComparison.Ordinal)
            ? [apiKey]
            : [apiKey, escaped];
    }

    private static string Target(Uri? uri) =>
        uri is null
            ? "(none)"
            : uri.IsAbsoluteUri
                ? uri.GetLeftPart(UriPartial.Path)
                : uri.OriginalString.Split('?', 2)[0];
}
