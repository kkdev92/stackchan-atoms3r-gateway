using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Models;

/// <summary>Parses tool calls embedded in response text.</summary>
/// <remarks>
/// Local models sometimes emit tool calls as tagged or untagged JSON in response text instead of
/// using <c>tool_calls</c>. This class converts several common formats to
/// <see cref="FunctionCallContent"/>.
/// </remarks>
internal static class ToolCallText
{
    // Accept common wrapper tags used by different models.
    private static readonly (string Open, string Close)[] Fences =
    [
        ("<tool_call>", "</tool_call>"),
        ("<tools>", "</tools>"),
        ("<|tool_call|>", "<|/tool_call|>"),
    ];

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The maximum number of characters retained while waiting for a closing tag or complete JSON value.</summary>
    /// <remarks>
    /// Candidates over the limit are returned as regular text. This bounds memory use and parsing
    /// time even when a response has no closing tag.
    /// </remarks>
    internal const int MaxPendingChars = 8192;

    /// <summary>The maximum number of tool calls converted from one response.</summary>
    /// <remarks>
    /// Converted calls are executed, so this limit prevents an oversized response from invoking a
    /// large number of capabilities.
    /// </remarks>
    internal const int MaxCallsPerResponse = 8;

    /// <summary>Extracts completed tool calls and regular text from a buffer.</summary>
    /// <param name="buffer">The working buffer. Completed portions are removed.</param>
    /// <param name="flush"><see langword="true"/> to finalize incomplete candidates as text at the end of a stream.</param>
    /// <param name="knownTools">
    /// Declared tool names. Untagged JSON is converted only when its name appears in this set.
    /// </param>
    internal static (List<FunctionCallContent> Calls, string Text) DrainBuffer(
        StringBuilder buffer,
        bool flush,
        IReadOnlySet<string>? knownTools = null)
    {
        var calls = new List<FunctionCallContent>();
        var text = new StringBuilder();

        var giveUp = flush || buffer.Length > MaxPendingChars;

        while (buffer.Length > 0)
        {
            if (calls.Count >= MaxCallsPerResponse)
            {
                text.Append(buffer);
                buffer.Clear();

                break;
            }

            var content = buffer.ToString();
            var open = FindOpen(content, out var fence);

            if (open < 0)
            {
                var bare = DrainBareJson(buffer, giveUp, knownTools, calls, text);

                if (bare == BareJsonOutcome.Consumed)
                {
                    continue;
                }

                if (bare == BareJsonOutcome.Wait)
                {
                    break;
                }

                // Hold back only the suffix that may be the beginning of an opening tag.
                var keep = giveUp ? 0 : PartialOpenLength(content);

                text.Append(content, 0, content.Length - keep);
                buffer.Remove(0, content.Length - keep);

                break;
            }

            text.Append(content, 0, open);

            var bodyStart = open + fence.Open.Length;
            var close = content.IndexOf(fence.Close, bodyStart, StringComparison.Ordinal);

            if (close < 0)
            {
                if (!giveUp)
                {
                    buffer.Remove(0, open);
                    break;
                }

                // Return an incomplete block as text instead of treating it as a tool call.
                text.Append(content, open, content.Length - open);
                buffer.Clear();

                break;
            }

            var body = content[bodyStart..close];
            var call = TryParseCall(body);

            if (call is not null)
            {
                calls.Add(call);
            }
            else
            {
                // Preserve an unparseable block as text so its content is not lost.
                text.Append(content, open, close + fence.Close.Length - open);
            }

            buffer.Remove(0, close + fence.Close.Length);
        }

        return (calls, text.ToString());
    }

    // To avoid executing regular JSON, convert an untagged value only at the start of a response
    // and only when it names a declared tool.
    private enum BareJsonOutcome
    {
        None,

        Consumed,

        Wait,
    }

    private static BareJsonOutcome DrainBareJson(
        StringBuilder buffer,
        bool flush,
        IReadOnlySet<string>? knownTools,
        List<FunctionCallContent> calls,
        StringBuilder text)
    {
        if (knownTools is null || knownTools.Count == 0)
        {
            return BareJsonOutcome.None;
        }

        var content = buffer.ToString();
        var start = 0;

        while (start < content.Length && char.IsWhiteSpace(content[start]))
        {
            start++;
        }

        if (start >= content.Length || content[start] != '{')
        {
            return BareJsonOutcome.None;
        }

        var body = content[start..];
        var end = FindJsonEnd(body);

        if (end <= 0)
        {
            return flush ? BareJsonOutcome.None : BareJsonOutcome.Wait;
        }

        var call = TryParseCall(body[..end]);

        if (call is null || !knownTools.Contains(call.Name))
        {
            return BareJsonOutcome.None;
        }

        text.Append(content, 0, start);
        calls.Add(call);
        buffer.Remove(0, start + end);

        return BareJsonOutcome.Consumed;
    }

    private static int FindOpen(string content, out (string Open, string Close) fence)
    {
        var best = -1;
        fence = Fences[0];

        foreach (var candidate in Fences)
        {
            var index = content.IndexOf(candidate.Open, StringComparison.Ordinal);

            if (index >= 0 && (best < 0 || index < best))
            {
                best = index;
                fence = candidate;
            }
        }

        return best;
    }

    /// <summary>Returns the length of a suffix that matches the beginning of an opening tag.</summary>
    /// <remarks>
    /// This prevents the device from speaking a fragment split in the middle of an opening tag.
    /// </remarks>
    private static int PartialOpenLength(string content)
    {
        foreach (var (open, _) in Fences)
        {
            var maximum = Math.Min(open.Length - 1, content.Length);

            for (var length = maximum; length > 0; length--)
            {
                if (string.CompareOrdinal(
                        content, content.Length - length, open, 0, length) == 0)
                {
                    return length;
                }
            }
        }

        return 0;
    }

    private static FunctionCallContent? TryParseCall(string body)
    {
        var trimmed = body.Trim();

        if (trimmed.Length == 0)
        {
            return null;
        }

        // Apply the same limit when a complete block arrives in one chunk so oversized arguments
        // are not passed to a capability.
        if (trimmed.Length > MaxPendingChars)
        {
            return null;
        }

        var end = FindJsonEnd(trimmed);

        if (end <= 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed[..end]);
            var root = document.RootElement;

            // Some models return one call in an array; use its first element.
            if (root.ValueKind == JsonValueKind.Array)
            {
                var items = root.EnumerateArray();

                if (!items.MoveNext())
                {
                    return null;
                }

                root = items.Current;
            }

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                name.GetString() is not { Length: > 0 } functionName)
            {
                return null;
            }

            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

            // Accept both "arguments" and "parameters", as models use either form.
            if ((root.TryGetProperty("arguments", out var argumentsElement) ||
                    root.TryGetProperty("parameters", out argumentsElement)) &&
                argumentsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in argumentsElement.EnumerateObject())
                {
                    arguments[property.Name] = ToClrValue(property.Value);
                }
            }

            // Models do not return an ID here, so derive a stable ID from the content.
            var callId = $"call_{Math.Abs(
                StringComparer.Ordinal.GetHashCode(functionName + trimmed[..end])):x8}";

            return new FunctionCallContent(callId, functionName, arguments);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Returns the end of the first JSON value, or 0 if it is incomplete.</summary>
    /// <remarks>
    /// Some output contains extra closing brackets after the JSON value, so only the first balanced
    /// value is passed to <see cref="JsonDocument"/>.
    /// </remarks>
    private static int FindJsonEnd(string text)
    {
        if (text.Length == 0 || (text[0] != '{' && text[0] != '['))
        {
            return 0;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;

                case '{':
                case '[':
                    depth++;
                    break;

                case '}':
                case ']':
                    depth--;

                    if (depth == 0)
                    {
                        return index + 1;
                    }

                    break;

                default:
                    break;
            }
        }

        return 0;
    }

    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Number => element.TryGetInt64(out var integer)
            ? integer
            : element.GetDouble(),
        _ => element.GetRawText(),
    };
}
