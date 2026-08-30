using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Models;

/// <summary>
/// Converts tool calls embedded in response text to <see cref="FunctionCallContent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Some OpenAI-compatible endpoints return model-generated tool calls as response text like the
/// following instead of structuring them as <c>tool_calls</c>.
/// </para>
/// <code>
/// &lt;tool_call&gt;
/// {"name": "set_stackchan_expression", "arguments": {"expression": "happy"}}
/// &lt;/tool_call&gt;
/// </code>
/// <para>
/// Place this client directly outside the model and inside <c>FunctionInvokingChatClient</c>. It
/// converts matching text into executable tool calls and leaves other text unchanged.
/// </para>
/// <para>
/// Some endpoints return the same call in both response text and <c>tool_calls</c>. Calls with the
/// same name and arguments are therefore deduplicated.
/// </para>
/// </remarks>
internal sealed class TextToolCallChatClient : DelegatingChatClient
{
    public TextToolCallChatClient(IChatClient innerClient)
        : base(innerClient)
    {
    }

    private static HashSet<string> CollectToolNames(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tool in tools)
        {
            if (tool is AIFunction function)
            {
                names.Add(function.Name);
            }
        }

        return names;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base
            .GetResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        var knownTools = CollectToolNames(options);

        foreach (var message in response.Messages)
        {
            var converted = ConvertContents(message.Contents, knownTools);

            if (converted is not null)
            {
                message.Contents = converted;
            }
        }

        return response;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A tool call can span multiple chunks. Instead of buffering the entire response, this method
    /// retains only a suffix that may begin a tag or JSON value and returns confirmed text immediately.
    /// </remarks>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new StringBuilder();
        var knownTools = CollectToolNames(options);

        // Deduplicate text-derived and structured calls by name and arguments.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Apply one execution limit to the entire response, regardless of call representation.
        var passed = 0;
        ChatResponseUpdate? last = null;

        var stream = base.GetStreamingResponseAsync(messages, options, cancellationToken);

        await foreach (var update in stream.ConfigureAwait(false))
        {
            last = update;

            var text = update.Text;

            if (string.IsNullOrEmpty(text))
            {
                var kept = KeepNewCalls(update.Contents, seen, ref passed);

                if (kept is null)
                {
                    yield return update;
                }
                else if (kept.Count > 0)
                {
                    yield return NewUpdate(update, kept);
                }

                continue;
            }

            buffer.Append(text);

            var (emitted, safeText) = ToolCallText.DrainBuffer(buffer, flush: false, knownTools);

            foreach (var call in emitted)
            {
                if (passed >= ToolCallText.MaxCallsPerResponse)
                {
                    // Drop only calls over the limit; regular response text is still returned.
                    break;
                }

                if (seen.Add(CallKey(call)))
                {
                    passed++;
                    yield return NewUpdate(update, call);
                }
            }

            if (!string.IsNullOrEmpty(safeText))
            {
                yield return NewUpdate(update, new TextContent(safeText));
            }
        }

        // Return incomplete tool-call candidates as regular text at the end of the response.
        if (buffer.Length > 0 && last is not null)
        {
            var (emitted, safeText) = ToolCallText.DrainBuffer(buffer, flush: true, knownTools);

            foreach (var call in emitted)
            {
                if (passed >= ToolCallText.MaxCallsPerResponse)
                {
                    break;
                }

                if (seen.Add(CallKey(call)))
                {
                    passed++;
                    yield return NewUpdate(last, call);
                }
            }

            if (!string.IsNullOrEmpty(safeText))
            {
                yield return NewUpdate(last, new TextContent(safeText));
            }
        }
    }

    private static List<AIContent>? ConvertContents(
        IList<AIContent> contents,
        IReadOnlySet<string> knownTools)
    {
        var buffer = new StringBuilder();
        var converted = new List<AIContent>();
        var changed = false;

        // Record structured calls first so matching text does not create duplicates.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var content in contents)
        {
            if (content is FunctionCallContent existing)
            {
                seen.Add(CallKey(existing));
            }
        }

        foreach (var content in contents)
        {
            if (content is not TextContent textContent)
            {
                converted.Add(content);
                continue;
            }

            buffer.Clear();
            buffer.Append(textContent.Text);

            var (calls, text) = ToolCallText.DrainBuffer(buffer, flush: true, knownTools);

            if (calls.Count == 0)
            {
                converted.Add(content);
                continue;
            }

            changed = true;

            if (!string.IsNullOrEmpty(text))
            {
                converted.Add(new TextContent(text));
            }

            foreach (var call in calls)
            {
                if (seen.Add(CallKey(call)))
                {
                    converted.Add(call);
                }
            }
        }

        return changed ? converted : null;
    }

    /// <summary>Creates a deduplication key from a tool name and its arguments.</summary>
    /// <remarks>Call IDs are not compared because the endpoint and this client generate different IDs.</remarks>
    private static string CallKey(FunctionCallContent call)
    {
        if (call.Arguments is not { Count: > 0 } arguments)
        {
            return call.Name;
        }

        var parts = new List<string>(arguments.Count);

        foreach (var pair in arguments)
        {
            parts.Add($"{pair.Key}={pair.Value}");
        }

        parts.Sort(StringComparer.Ordinal);

        return call.Name + '\0' + string.Join('\u0001', parts);
    }

    private static List<AIContent>? KeepNewCalls(
        IList<AIContent> contents,
        HashSet<string> seen,
        ref int passed)
    {
        List<AIContent>? kept = null;

        for (var index = 0; index < contents.Count; index++)
        {
            var content = contents[index];
            var drop = false;

            if (content is FunctionCallContent call)
            {
                drop = !seen.Add(CallKey(call)) || passed >= ToolCallText.MaxCallsPerResponse;

                if (!drop)
                {
                    passed++;
                }
            }

            if (drop && kept is null)
            {
                kept = new List<AIContent>(contents.Count);

                for (var earlier = 0; earlier < index; earlier++)
                {
                    kept.Add(contents[earlier]);
                }

                continue;
            }

            if (!drop)
            {
                kept?.Add(content);
            }
        }

        return kept;
    }

    private static ChatResponseUpdate NewUpdate(
        ChatResponseUpdate source,
        AIContent content) => NewUpdate(source, [content]);

    private static ChatResponseUpdate NewUpdate(
        ChatResponseUpdate source,
        IList<AIContent> contents) =>
        new(source.Role, contents)
        {
            AuthorName = source.AuthorName,
            ConversationId = source.ConversationId,
            CreatedAt = source.CreatedAt,
            MessageId = source.MessageId,
            ModelId = source.ModelId,
            RawRepresentation = source.RawRepresentation,
            ResponseId = source.ResponseId,
        };
}
