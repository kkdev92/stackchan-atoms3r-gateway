namespace Kkdev92.StackChan.Gateway.Providers.Http;

/// <summary>
/// Reads provider HTTP responses while enforcing a byte limit.
/// </summary>
/// <remarks>
/// The limit is based on bytes actually read rather than trusting <c>Content-Length</c>, so it also
/// applies to chunked responses and incorrect length headers. The internal buffer may hold at most
/// one additional read while detecting that the limit was exceeded.
/// </remarks>
public static class ProviderResponse
{
    /// <summary>
    /// Reads a response body up to a limit and reports a non-retryable error when exceeded.
    /// </summary>
    /// <param name="content">HTTP response content to read. This method does not dispose it.</param>
    /// <param name="limit">Maximum allowed bytes; must be at least 1.</param>
    /// <param name="whenTooLarge">
    /// Safe message reported to the device when the limit is exceeded.
    /// </param>
    /// <param name="cancellationToken">Token that signals cancellation of reading.</param>
    /// <returns>All bytes read from the response body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1.</exception>
    /// <exception cref="Abstractions.ProviderException">The response body exceeds <paramref name="limit"/>.</exception>
    public static async Task<byte[]> ReadAtMostAsync(
        HttpContent content,
        int limit,
        string whenTooLarge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            // Read one more chunk after reaching the limit to distinguish an exact-length body from overflow.
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];

            while (buffer.Length <= limit)
            {
                var read = await stream
                    .ReadAsync(chunk.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    return buffer.ToArray();
                }

                buffer.Write(chunk, 0, read);
            }

            // Stop reading as soon as the limit is exceeded.
            throw ProviderEndpoint.Unavailable(
                whenTooLarge,
                new InvalidOperationException(
                    $"the provider sent more than {limit} bytes"),
                retryable: false);
        }
    }
}
