using System.Diagnostics;
using System.Text;
using Kkdev92.StackChan.Gateway.TestKit;

namespace StackChan.DeviceSim;

/// <summary>Measurements from one conversation request.</summary>
internal sealed record ConverseResult(
    int Status,
    string? ContentType,
    byte[] Body,
    string? Reason,
    TimeSpan ToHeaders,
    TimeSpan? ToTranscript,
    TimeSpan? ToFirstAudio,
    TimeSpan MaxGap,
    TimeSpan Total,
    IReadOnlyList<string> Violations,
    bool Disconnected)
{
    public string? Finished => SseWire.Events(Body)
        .Where(wire => wire.Name == "conversation.finished")
        .Select(wire => wire.Payload.TryGetProperty("reason", out var reason)
            ? reason.GetString()
            : null)
        .LastOrDefault();

    public string? ErrorCode => SseWire.Events(Body)
        .Where(wire => wire.Name == "error.raised")
        .Select(wire => wire.Payload.TryGetProperty("code", out var code)
            ? code.GetString()
            : null)
        .LastOrDefault();
}

/// <summary>Sends a conversation request and measures response and event timing.</summary>
/// <remarks>
/// Every scenario uses this implementation, so measurements and conformance criteria are consistent.
/// </remarks>
internal static class Converse
{
    /// <summary>Runs one conversation request.</summary>
    /// <param name="request">The request sent to the gateway.</param>
    /// <param name="slowReadMs">The delay after each read, in milliseconds. A value of 0 adds no delay.</param>
    /// <param name="disconnectAfterMs">
    /// Disconnect from the client after this interval. A value of 0 reads through completion.
    /// </param>
    /// <param name="disconnectOnFirstAudio">
    /// Disconnect immediately after the first audio event. This reproduces a disconnect during audio
    /// transmission without depending on response latency.
    /// </param>
    /// <param name="expectedTexts">
    /// Strings expected as UTF-8 in the response body. No text check is performed when omitted.
    /// </param>
    public static async Task<ConverseResult> RunAsync(
        HttpRequestMessage request,
        int slowReadMs = 0,
        int disconnectAfterMs = 0,
        bool disconnectOnFirstAudio = false,
        IReadOnlyList<string>? expectedTexts = null)
    {
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        var started = Stopwatch.StartNew();

        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        var toHeaders = started.Elapsed;
        var contentType = response.Content.Headers.ContentType?.ToString();

        if (!response.IsSuccessStatusCode)
        {
            var reason = await response.Content.ReadAsStringAsync();

            return new ConverseResult(
                (int)response.StatusCode, contentType, [], reason.Trim(),
                toHeaders, null, null, TimeSpan.Zero, started.Elapsed, [], false);
        }

        var body = new MemoryStream();
        var buffer = new byte[4096];
        var lastWrite = started.Elapsed;
        var maxGap = TimeSpan.Zero;
        TimeSpan? toTranscript = null;
        TimeSpan? toFirstAudio = null;
        var disconnected = false;

        // Retain only enough text to find event names across chunk boundaries instead of decoding the full body repeatedly.
        var carry = "";
        const int carryLength = 32;

        await using (var stream = await response.Content.ReadAsStreamAsync())
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer);

                if (read == 0)
                {
                    break;
                }

                // Simulate a slow client to exercise gateway backpressure.
                if (slowReadMs > 0)
                {
                    await Task.Delay(slowReadMs);
                }

                var now = started.Elapsed;
                var gap = now - lastWrite;

                if (gap > maxGap)
                {
                    maxGap = gap;
                }

                lastWrite = now;
                body.Write(buffer, 0, read);

                if (disconnectAfterMs > 0 && now.TotalMilliseconds >= disconnectAfterMs)
                {
                    // Dispose the stream to close the connection and signal device disconnection to the gateway.
                    disconnected = true;
                    break;
                }

                if (disconnectOnFirstAudio && toFirstAudio is not null)
                {
                    disconnected = true;
                    break;
                }

                if (toTranscript is not null && toFirstAudio is not null)
                {
                    continue;
                }

                var window = carry + Encoding.UTF8.GetString(buffer, 0, read);

                if (toTranscript is null &&
                    window.Contains("conversation.text", StringComparison.Ordinal))
                {
                    toTranscript = now;
                }

                if (toFirstAudio is null &&
                    window.Contains("reply.audio", StringComparison.Ordinal))
                {
                    toFirstAudio = now;
                }

                carry = window.Length <= carryLength ? window : window[^carryLength..];
            }
        }

        var bytes = body.ToArray();

        // A response interrupted by the client is incomplete and cannot be checked for conformance.
        var violations = disconnected
            ? []
            : ConformanceChecks.Run(contentType, bytes, expectedTexts)
                .Select(violation => violation.ToString())
                .ToArray();

        return new ConverseResult(
            (int)response.StatusCode, contentType, bytes, null,
            toHeaders, toTranscript, toFirstAudio, maxGap, started.Elapsed,
            violations, disconnected);
    }
}
