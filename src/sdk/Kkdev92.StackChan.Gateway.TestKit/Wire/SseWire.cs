using System.Text;
using System.Text.Json;

namespace Kkdev92.StackChan.Gateway.TestKit;

/// <summary>Represents one SSE record separated by a blank line.</summary>
/// <param name="Lines">The lines in the record.</param>
/// <param name="ByteLength">The record size in bytes, including the blank-line delimiter and <c>data: </c>.</param>
public sealed record SseRecord(IReadOnlyList<string> Lines, int ByteLength)
{
    /// <summary>Gets whether this is an SSE comment, such as a keep-alive record.</summary>
    public bool IsComment => Lines.Count == 1 && Lines[0].StartsWith(':');

    /// <summary>Gets JSON from the <c>data:</c> field, or <see langword="null"/> when not applicable.</summary>
    public string? Json =>
        Lines.Count == 1 && Lines[0].StartsWith("data: ", StringComparison.Ordinal)
            ? Lines[0]["data: ".Length..]
            : null;
}

/// <summary>Represents an event envelope received over SSE.</summary>
/// <param name="Name">The event name.</param>
/// <param name="Payload">The event payload.</param>
/// <param name="Envelope">The full envelope, including its version and type.</param>
/// <param name="Json">The undecoded JSON.</param>
/// <param name="JsonByteLength">The UTF-8 byte length of the JSON.</param>
public sealed record WireEvent(
    string Name,
    JsonElement Payload,
    JsonElement Envelope,
    string Json,
    int JsonByteLength)
{
    /// <summary>Parses JSON as an event envelope.</summary>
    /// <param name="json">The JSON without the <c>data: </c> prefix.</param>
    /// <param name="wire">The parsed event on success.</param>
    /// <param name="error">The error message on failure.</param>
    public static bool TryRead(string json, out WireEvent? wire, out string? error)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var envelope = document.RootElement.Clone();

            envelope.TryGetProperty("name", out var name);
            envelope.TryGetProperty("payload", out var payload);

            wire = new WireEvent(
                name.ValueKind == JsonValueKind.String ? name.GetString() ?? "" : "",
                payload,
                envelope,
                json,
                Encoding.UTF8.GetByteCount(json));
            error = null;

            return true;
        }
        catch (JsonException exception)
        {
            wire = null;
            error = exception.Message;

            return false;
        }
    }
}

/// <summary>
/// Parses SSE responses in their wire format.
/// </summary>
/// <remarks>
/// Use this helper to check issues that DTO-only assertions can miss, including JSON escaping,
/// newlines, event boundaries, and Base64 encoding.
/// </remarks>
public static class SseWire
{
    /// <summary>Expression markers recognized by AtomS3R.</summary>
    public static readonly string[] AllowedExpressionMarkers =
        ["neutral", "happy", "sad", "doubt", "sleepy", "angry"];

    /// <summary>Splits a UTF-8 SSE response into records.</summary>
    public static IReadOnlyList<SseRecord> Parse(byte[] body) =>
        Parse(Encoding.UTF8.GetString(body));

    /// <summary>Splits an SSE response into records.</summary>
    public static IReadOnlyList<SseRecord> Parse(string text)
    {
        var records = new List<SseRecord>();

        foreach (var chunk in text.Split("\n\n"))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            records.Add(new SseRecord(
                chunk.Split('\n'),
                Encoding.UTF8.GetByteCount(chunk) + 2));
        }

        return records;
    }

    /// <summary>Returns only event envelopes that can be parsed.</summary>
    public static IReadOnlyList<WireEvent> Events(byte[] body) =>
        Events(Parse(body));

    /// <summary>Returns event envelopes that can be parsed from SSE records.</summary>
    public static IReadOnlyList<WireEvent> Events(IReadOnlyList<SseRecord> records)
    {
        var events = new List<WireEvent>();

        foreach (var record in records)
        {
            if (record.Json is { } json && WireEvent.TryRead(json, out var wire, out _))
            {
                events.Add(wire!);
            }
        }

        return events;
    }
}
