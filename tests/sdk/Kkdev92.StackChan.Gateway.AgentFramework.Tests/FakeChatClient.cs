using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Tests;

/// <summary>
/// Test ChatClient that records the requests passed to the model and returns preset fragments.
/// </summary>
/// <remarks>
/// Verifies history, reasoning-text removal, and tool calls without starting a local model.
/// </remarks>
internal sealed class FakeChatClient : IChatClient
{
    /// <summary>Response fragments returned for each call.</summary>
    public List<IReadOnlyList<AIContent>> Rounds { get; } = [];

    /// <summary>Messages received on each call.</summary>
    public List<List<ChatMessage>> Seen { get; } = [];

    /// <summary>ChatOptions received on each call.</summary>
    public List<ChatOptions?> Options { get; } = [];

    public int Calls => Seen.Count;

    public bool ObservedCancellation { get; private set; }

    /// <summary>When set, the response waits until it is completed externally.</summary>
    public TaskCompletionSource? Block { get; set; }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var round = Seen.Count;
        Seen.Add([.. messages]);
        Options.Add(options);

        if (Block is not null)
        {
            try
            {
                await Block.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }

        var contents = round < Rounds.Count
            ? Rounds[round]
            : [new TextContent("[neutral]はい。")];

        foreach (var content in contents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [content]);
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Seen.Add([.. messages]);
        Options.Add(options);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "はい。")));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
