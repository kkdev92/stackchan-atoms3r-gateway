using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Kkdev92.StackChan.Gateway.Abstractions.Telemetry;
using Microsoft.Extensions.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Models;

/// <summary>
/// Prefetches capabilities whose trigger phrases match and passes their results to the model.
/// </summary>
/// <remarks>
/// <para>
/// Small local models do not always select a required tool with <c>tool_choice: auto</c>. When a
/// user utterance contains a declared trigger for a read-only capability with no required arguments,
/// this client invokes it without waiting for the model to choose it.
/// </para>
/// <para>
/// Results are added only to the current request as tool-call and tool-result messages. If no trigger
/// matches, the messages remain unchanged and normal model-driven tool selection is preserved.
/// </para>
/// <para>
/// This client must be placed inside <c>FunctionInvokingChatClient</c>. If placed outside, normal tool
/// invocation starts before the model can see the prefetched result.
/// </para>
/// </remarks>
internal sealed class CapabilityPrefetchChatClient : DelegatingChatClient
{
    // Match runtime sentence boundaries and avoid appending punctuation to an already closed sentence.
    private static readonly char[] SentenceEnds = ['。', '．', '！', '？', '!', '?', '.'];

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _triggers;

    private readonly Action<string, Exception>? _onFailed;

    public CapabilityPrefetchChatClient(
        IChatClient innerClient,
        IReadOnlyDictionary<string, IReadOnlyList<string>> triggers,
        Action<string, Exception>? onFailed = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(triggers);

        _triggers = triggers;
        _onFailed = onFailed;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (prepared, adjusted) = await PrepareAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        return await base.GetResponseAsync(prepared, adjusted, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (prepared, adjusted) = await PrepareAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        var updates = base.GetStreamingResponseAsync(prepared, adjusted, cancellationToken);

        await foreach (var update in updates.ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async Task<(IEnumerable<ChatMessage> Messages, ChatOptions? Options)> PrepareAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        if (options?.Tools is not { Count: > 0 } || _triggers.Count == 0)
        {
            return (messages, options);
        }

        var list = messages as IReadOnlyList<ChatMessage> ?? [.. messages];

        // Do not add prefetch results to a follow-up request after a normal tool call.
        if (HasToolResult(list))
        {
            return (messages, options);
        }

        var asked = LastUserText(list);

        if (asked is null)
        {
            return (messages, options);
        }

        // Use the first tool name as the call name because multiple results are combined into one result.
        string? representative = null;
        var results = new List<string>(MaxPrefetched);

        foreach (var name in MatchAll(asked))
        {
            if (Find(options.Tools, name) is not { } function)
            {
                continue;
            }

            var result = await RunAsync(function, _onFailed, cancellationToken)
                .ConfigureAwait(false);

            if (result is null)
            {
                continue;
            }

            representative ??= name;
            results.Add(result);
        }

        if (results.Count == 0)
        {
            return (messages, options);
        }

        // Some local models mishandle consecutive tool results, so combine them into one message.
        // Results remain in the order their triggers appeared in the user utterance.
        var callId = $"pre_{representative}";

        var prepared = new List<ChatMessage>(list.Count + 2);
        prepared.AddRange(list);
        prepared.Add(new ChatMessage(
            ChatRole.Assistant, [new FunctionCallContent(callId, representative!)]));
        prepared.Add(new ChatMessage(
            ChatRole.Tool, [new FunctionResultContent(callId, Combine(results))]));

        // Disable tools so the model cannot invoke the same capability again in this request.
        var adjusted = options.Clone();
        adjusted.ToolMode = ChatToolMode.None;

        return (prepared, adjusted);
    }

    // Separate results as complete sentences on new lines. Avoid symbols such as slashes because a
    // model may copy them verbatim into a response that the device then speaks.
    private static string Combine(List<string> results)
    {
        if (results.Count == 1)
        {
            return results[0];
        }

        var text = new StringBuilder();

        foreach (var result in results)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(result);

            // Add a terminator so adjacent results are not interpreted as one sentence.
            if (!SentenceEnds.Contains(result[^1]))
            {
                text.Append('。');
            }
        }

        return text.ToString();
    }

    /// <summary>Invokes a capability and converts its result to text for the model.</summary>
    /// <remarks>On failure, this reports the exception and returns <see langword="null"/> without stopping the turn.</remarks>
    private static async Task<string?> RunAsync(
        AIFunction function,
        Action<string, Exception>? onFailed,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await function
                .InvokeAsync(new AIFunctionArguments(), cancellationToken)
                .ConfigureAwait(false);

            return value switch
            {
                null => null,
                string text => text,
                JsonElement json => json.ValueKind == JsonValueKind.String
                    ? json.GetString()
                    : json.GetRawText(),
                _ => value.ToString(),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GatewayTelemetry.CapabilityCalled(function.Name, "prefetch-failed");

            try
            {
                onFailed?.Invoke(function.Name, exception);
            }
            catch (Exception thrown) when (thrown is not OperationCanceledException)
            {
            }

            return null;
        }
    }

    /// <summary>Finds a capability with no required arguments by tool name.</summary>
    /// <remarks>Tools with required arguments are not prefetched because their values cannot be determined safely from an utterance.</remarks>
    private static AIFunction? Find(IEnumerable<AITool> tools, string name)
    {
        foreach (var tool in tools)
        {
            if (tool is AIFunction function &&
                string.Equals(function.Name, name, StringComparison.Ordinal) &&
                !NeedsArguments(function))
            {
                return function;
            }
        }

        return null;
    }

    private static bool NeedsArguments(AIFunction function) =>
        function.JsonSchema.ValueKind == JsonValueKind.Object &&
        function.JsonSchema.TryGetProperty("required", out var required) &&
        required.ValueKind == JsonValueKind.Array &&
        required.GetArrayLength() > 0;

    /// <summary>The maximum number of capabilities prefetched for one utterance.</summary>
    /// <remarks>
    /// Prefetch performs external I/O before the first model response, so this limit prevents an
    /// utterance with many triggers from delaying response start excessively.
    /// </remarks>
    private const int MaxPrefetched = 3;

    /// <summary>Returns matching capability names in the order their triggers appear in the utterance.</summary>
    /// <remarks>
    /// Matches at the same position are ordered by name so the result does not depend on dictionary enumeration order.
    /// </remarks>
    private IReadOnlyList<string> MatchAll(string asked)
    {
        List<(int At, string Name)>? hits = null;

        foreach (var (name, triggers) in _triggers)
        {
            var at = -1;

            foreach (var trigger in triggers)
            {
                var found = asked.IndexOf(trigger, StringComparison.OrdinalIgnoreCase);

                if (found >= 0 && (at < 0 || found < at))
                {
                    at = found;
                }
            }

            if (at >= 0)
            {
                (hits ??= []).Add((at, name));
            }
        }

        if (hits is null)
        {
            return [];
        }

        hits.Sort(static (left, right) => left.At != right.At
            ? left.At.CompareTo(right.At)
            : string.CompareOrdinal(left.Name, right.Name));

        return [.. hits.Take(MaxPrefetched).Select(static hit => hit.Name)];
    }

    private static bool HasToolResult(IReadOnlyList<ChatMessage> messages)
    {
        // Inspect only messages after the latest user utterance so an older tool result does not
        // suppress prefetch. FunctionInvokingChatClient adds its result within this range on reentry.
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];

            if (message.Role == ChatRole.User)
            {
                return false;
            }

            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? LastUserText(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index].Role == ChatRole.User &&
                messages[index].Text is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }
}
