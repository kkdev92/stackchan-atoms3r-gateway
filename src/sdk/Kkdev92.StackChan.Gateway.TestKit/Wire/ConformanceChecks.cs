using System.Text;
using System.Text.Json;
using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.TestKit;

/// <summary>Represents a violation found by a protocol conformance check.</summary>
/// <param name="Number">The check number, from 1 through 13.</param>
/// <param name="Detail">Details of the violation.</param>
public sealed record ConformanceViolation(int Number, string Detail)
{
    /// <inheritdoc />
    public override string ToString() => $"#{Number}: {Detail}";
}

/// <summary>Checks whether response bytes satisfy the AtomS3R protocol requirements.</summary>
/// <remarks>
/// These checks are independent of any HTTP server implementation and can inspect both real
/// responses and byte arrays created by tests.
/// </remarks>
public static class ConformanceChecks
{
    /// <summary>The maximum size of one SSE event, in bytes.</summary>
    public const int MaxEventBytes = DeviceLimits.MaxEventBytes;

    /// <summary>The byte length of the SSE <c>data: </c> prefix.</summary>
    /// <remarks>
    /// The firmware receives the complete line, including <c>data: </c>, in a fixed-size buffer.
    /// The size limit therefore applies to the full prefixed line, not only the JSON.
    /// </remarks>
    public const int DataFieldPrefixBytes = DeviceLimits.DataFieldPrefixBytes;

    /// <summary>The maximum PCM payload size in one SSE event, in bytes.</summary>
    public const int MaxPcmBytes = DeviceLimits.MaxPcmBytes;

    /// <summary>The maximum size of a <c>text</c> field, in bytes.</summary>
    public const int MaxTextBytes = DeviceLimits.MaxTextBytes;

    /// <summary>The sample rate accepted by AtomS3R.</summary>
    public const int ExpectedRate = PcmAudio.CanonicalSampleRate;

    /// <summary>The Content-Type required for conversation responses.</summary>
    public const string ExpectedContentType = "text/event-stream; charset=utf-8";

    /// <summary>Runs all conformance checks and returns detected violations.</summary>
    /// <param name="contentType">The response Content-Type.</param>
    /// <param name="body">The response body bytes.</param>
    /// <param name="expectedUtf8Texts">Strings expected as UTF-8 rather than Unicode escapes.</param>
    public static IReadOnlyList<ConformanceViolation> Run(
        string? contentType,
        byte[] body,
        IEnumerable<string>? expectedUtf8Texts = null)
    {
        var violations = new List<ConformanceViolation>();

        var text = Encoding.UTF8.GetString(body);
        var records = SseWire.Parse(text);

        CheckContentType(contentType, violations);
        CheckFraming(text, records, violations);

        var events = ReadEvents(records, violations);

        CheckEnvelopes(events, violations);
        CheckNoUnicodeEscapes(events, text, expectedUtf8Texts, violations);
        CheckEventSize(events, violations);
        CheckAudio(events, violations);
        CheckTexts(events, violations);
        CheckCompletion(events, violations);

        return violations;
    }

    /// <summary>1. Checks the Content-Type.</summary>
    private static void CheckContentType(
        string? contentType,
        List<ConformanceViolation> violations)
    {
        if (!string.Equals(contentType, ExpectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add(new ConformanceViolation(
                1, $"Content-Type is '{contentType}'; expected '{ExpectedContentType}'."));
        }
    }

    /// <summary>2. Checks SSE event boundaries and line endings.</summary>
    private static void CheckFraming(
        string text,
        IReadOnlyList<SseRecord> records,
        List<ConformanceViolation> violations)
    {
        if (text.Length > 0 && !text.EndsWith("\n\n", StringComparison.Ordinal))
        {
            violations.Add(new ConformanceViolation(2, "The final SSE event does not end with a blank line."));
        }

        if (text.Contains('\r'))
        {
            violations.Add(new ConformanceViolation(2, "SSE line endings contain CR characters; use LF only."));
        }

        foreach (var record in records)
        {
            if (record.IsComment)
            {
                continue;
            }

            if (record.Lines.Count != 1)
            {
                violations.Add(new ConformanceViolation(
                    2, $"One SSE event is split across {record.Lines.Count} lines."));
                continue;
            }

            if (record.Json is null)
            {
                violations.Add(new ConformanceViolation(
                    2, $"An SSE line does not begin with 'data: ': {Head(record.Lines[0])}"));
            }
        }
    }

    private static List<WireEvent> ReadEvents(
        IReadOnlyList<SseRecord> records,
        List<ConformanceViolation> violations)
    {
        var events = new List<WireEvent>();

        foreach (var record in records)
        {
            if (record.Json is not { } json)
            {
                continue;
            }

            if (WireEvent.TryRead(json, out var wire, out var error))
            {
                events.Add(wire!);
            }
            else
            {
                violations.Add(new ConformanceViolation(
                    3, $"The event envelope could not be parsed as JSON: {error}"));
            }
        }

        return events;
    }

    /// <summary>3. Checks required event-envelope fields.</summary>
    private static void CheckEnvelopes(
        List<WireEvent> events,
        List<ConformanceViolation> violations)
    {
        foreach (var wire in events)
        {
            var root = wire.Envelope;

            if (!root.TryGetProperty("v", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                version.GetInt32() != 1)
            {
                violations.Add(new ConformanceViolation(3, "The event envelope's v field is not 1."));
            }

            if (!root.TryGetProperty("kind", out var kind) ||
                kind.ValueKind != JsonValueKind.String ||
                kind.GetString() != "event")
            {
                violations.Add(new ConformanceViolation(3, "The event envelope's kind field is not 'event'."));
            }

            if (string.IsNullOrEmpty(wire.Name))
            {
                violations.Add(new ConformanceViolation(3, "The event envelope's name field is not a non-empty string."));
            }

            if (wire.Payload.ValueKind != JsonValueKind.Object)
            {
                violations.Add(new ConformanceViolation(
                    3, $"The payload for {wire.Name} is not a JSON object."));
            }
        }
    }

    /// <summary>4. Checks that no Unicode escapes remain.</summary>
    private static void CheckNoUnicodeEscapes(
        IReadOnlyList<WireEvent> events,
        string text,
        IEnumerable<string>? expectedUtf8Texts,
        List<ConformanceViolation> violations)
    {
        foreach (var wire in events)
        {
            if (wire.Json.Contains("\\u", StringComparison.Ordinal))
            {
                violations.Add(new ConformanceViolation(
                    4, $"{wire.Name} contains a \\u Unicode escape."));
            }
        }

        if (expectedUtf8Texts is null)
        {
            return;
        }

        foreach (var expected in expectedUtf8Texts)
        {
            if (!text.Contains(expected, StringComparison.Ordinal))
            {
                violations.Add(new ConformanceViolation(
                    4, $"Expected UTF-8 text '{expected}' was not found."));
            }
        }
    }

    /// <summary>5. Checks the byte size of each SSE event.</summary>
    private static void CheckEventSize(
        IReadOnlyList<WireEvent> events,
        List<ConformanceViolation> violations)
    {
        foreach (var wire in events)
        {
            var line = DataFieldPrefixBytes + wire.JsonByteLength;

            if (line > MaxEventBytes)
            {
                violations.Add(new ConformanceViolation(
                    5, $"The {wire.Name} SSE line is {line} bytes ({wire.JsonByteLength} bytes of JSON)."));
            }
        }
    }

    /// <summary>6-8, 11. Checks audio-event sequence numbers, format, and final flags.</summary>
    private static void CheckAudio(
        IReadOnlyList<WireEvent> events,
        List<ConformanceViolation> violations)
    {
        var audio = events.Where(wire => wire.Name == "reply.audio").ToList();

        if (audio.Count == 0)
        {
            return;
        }

        long? previous = null;
        var lastFlags = new List<int>();

        for (var index = 0; index < audio.Count; index++)
        {
            var payload = audio[index].Payload;

            // AtomS3R treats a missing seq value as a stream error.
            if (!payload.TryGetProperty("seq", out var seqElement) ||
                seqElement.ValueKind != JsonValueKind.Number)
            {
                violations.Add(new ConformanceViolation(6, "reply.audio has no numeric seq value."));
            }
            else
            {
                var seq = seqElement.GetInt64();

                if (previous is null)
                {
                    if (seq != 0)
                    {
                        violations.Add(new ConformanceViolation(
                            6, $"The first reply.audio seq is {seq}; it must start at 0."));
                    }
                }
                else if (seq != previous + 1)
                {
                    violations.Add(new ConformanceViolation(
                        6, $"reply.audio seq jumps from {previous} to {seq}."));
                }

                previous = seq;
            }

            if (!payload.TryGetProperty("rate", out var rate) ||
                rate.ValueKind != JsonValueKind.Number ||
                rate.GetInt32() != ExpectedRate)
            {
                violations.Add(new ConformanceViolation(
                    7, $"reply.audio rate is {ReadRaw(payload, "rate")}; expected {ExpectedRate}."));
            }

            if (!payload.TryGetProperty("pcm", out var pcm) ||
                pcm.ValueKind != JsonValueKind.String)
            {
                violations.Add(new ConformanceViolation(8, "reply.audio has no string pcm value."));
            }
            else
            {
                var encoded = pcm.GetString() ?? "";

                // The firmware passes the JSON string value to the Base64 decoder without unescaping it,
                // so the raw JSON token must also be checked.
                CheckRawBase64(pcm.GetRawText(), violations);

                if (encoded.Length == 0)
                {
                    // Empty strings are valid for text-only and terminal events.
                }
                else if (!TryDecode(encoded, out var bytes))
                {
                    violations.Add(new ConformanceViolation(8, "pcm cannot be decoded as Base64."));
                }
                else if (bytes > MaxPcmBytes)
                {
                    violations.Add(new ConformanceViolation(
                        8, $"pcm is {bytes} bytes; the limit is {MaxPcmBytes} bytes."));
                }
                else if (bytes % 2 != 0)
                {
                    violations.Add(new ConformanceViolation(
                        8, $"pcm is {bytes} bytes; 16-bit PCM requires an even byte count."));
                }
            }

            if (payload.TryGetProperty("last", out var last) &&
                last.ValueKind == JsonValueKind.True)
            {
                lastFlags.Add(index);
            }
        }

        if (lastFlags.Count == 0)
        {
            violations.Add(new ConformanceViolation(11, "No reply.audio event has last=true."));
        }
        else if (lastFlags.Count > 1)
        {
            violations.Add(new ConformanceViolation(
                11, $"{lastFlags.Count} reply.audio events have last=true."));
        }
        else if (lastFlags[0] != audio.Count - 1)
        {
            violations.Add(new ConformanceViolation(
                11, "last=true is not set on the final reply.audio event."));
        }
    }

    /// <summary>9, 10, 13. Checks text size, duplication, and expression markers.</summary>
    private static void CheckTexts(
        IReadOnlyList<WireEvent> events,
        List<ConformanceViolation> violations)
    {
        // Detect only repeated text in consecutive chunks of the same sentence. Identical text in
        // separated events is allowed because the agent may intentionally repeat a sentence.
        string? previousText = null;

        foreach (var wire in events)
        {
            if (wire.Payload.ValueKind != JsonValueKind.Object ||
                !wire.Payload.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                // An audio event without text is a continuation chunk for the same sentence.
                if (wire.Name == "reply.audio")
                {
                    previousText = null;
                }

                continue;
            }

            var text = textElement.GetString() ?? "";
            var bytes = Encoding.UTF8.GetByteCount(text);

            if (bytes > MaxTextBytes)
            {
                violations.Add(new ConformanceViolation(
                    9, $"The text in {wire.Name} is {bytes} bytes; the limit is {MaxTextBytes} bytes."));
            }

            if (wire.Name == "reply.audio")
            {
                if (string.Equals(previousText, text, StringComparison.Ordinal))
                {
                    violations.Add(new ConformanceViolation(
                        10, $"Consecutive reply.audio events contain the same text: {Head(text)}"));
                }

                previousText = text;
            }

            if (text.StartsWith('[') && text.IndexOf(']', StringComparison.Ordinal) is var close &&
                close > 1)
            {
                var marker = text[1..close];

                if (!SseWire.AllowedExpressionMarkers.Contains(marker, StringComparer.Ordinal))
                {
                    violations.Add(new ConformanceViolation(
                        13, $"The expression marker is not recognized by AtomS3R: [{marker}]"));
                }
            }
        }
    }

    /// <summary>12. Checks that <c>conversation.finished</c> is at the end of the stream.</summary>
    private static void CheckCompletion(
        List<WireEvent> events,
        List<ConformanceViolation> violations)
    {
        var finished = events
            .Select((wire, index) => (wire, index))
            .Where(pair => pair.wire.Name == "conversation.finished")
            .ToList();

        if (finished.Count == 0)
        {
            violations.Add(new ConformanceViolation(12, "conversation.finished is missing."));
            return;
        }

        if (finished.Count > 1)
        {
            violations.Add(new ConformanceViolation(
                12, $"There are {finished.Count} conversation.finished events."));
        }

        if (finished[^1].index != events.Count - 1)
        {
            violations.Add(new ConformanceViolation(
                12, "An event appears after conversation.finished."));
        }
    }

    /// <summary>
    /// Checks whether <c>pcm</c> in JSON uses a Base64 format accepted by the firmware.
    /// </summary>
    /// <remarks>
    /// The firmware decoder rejects whitespace, newlines, and JSON escapes.
    /// <see cref="Convert.TryFromBase64String"/> ignores whitespace, so the raw JSON token is checked separately.
    /// </remarks>
    private static void CheckRawBase64(string rawToken, List<ConformanceViolation> violations)
    {
        if (rawToken.Length < 2 || rawToken[0] != '"' || rawToken[^1] != '"')
        {
            violations.Add(new ConformanceViolation(8, "pcm is not a JSON string."));
            return;
        }

        var value = rawToken[1..^1];

        if (value.Length % 4 != 0)
        {
            violations.Add(new ConformanceViolation(
                8, $"The pcm Base64 length, {value.Length}, is not a multiple of 4."));
        }

        var padding = 0;

        foreach (var c in value)
        {
            if (c == '=')
            {
                if (++padding > 2)
                {
                    violations.Add(new ConformanceViolation(8, "pcm has more than two Base64 padding characters."));
                    return;
                }

                continue;
            }

            if (padding > 0)
            {
                violations.Add(new ConformanceViolation(
                    8, $"pcm contains a character after Base64 padding: {Describe(c)}"));
                return;
            }

            if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '/')
            {
                violations.Add(new ConformanceViolation(
                    8, $"pcm contains a character that is invalid in Base64: {Describe(c)}"));
                return;
            }
        }
    }

    private static string Describe(char c) =>
        char.IsControl(c) || c == ' '
            ? $"U+{(int)c:X4}"
            : $"'{c}'";

    private static bool TryDecode(string encoded, out int byteCount)
    {
        var buffer = new byte[encoded.Length];

        if (Convert.TryFromBase64String(encoded, buffer, out byteCount))
        {
            return true;
        }

        byteCount = 0;
        return false;
    }

    private static string ReadRaw(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value)
            ? value.GetRawText()
            : "(missing)";

    private static string Head(string text) =>
        text.Length <= 40 ? text : text[..40] + "…";
}
