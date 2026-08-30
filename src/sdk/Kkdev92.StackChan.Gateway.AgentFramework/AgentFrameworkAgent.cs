using System.ClientModel;
using System.Runtime.CompilerServices;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.AgentFramework.Models;
using Kkdev92.StackChan.Gateway.AgentFramework.Sessions;
using Kkdev92.StackChan.Gateway.AgentFramework.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Kkdev92.StackChan.Gateway.AgentFramework;

/// <summary>Generates responses with Microsoft Agent Framework.</summary>
/// <remarks>
/// The public API does not expose framework-specific types and returns only text fragments suitable
/// for speech. Reasoning content and tool calls are not sent to the device. The runtime handles
/// sentence segmentation and speech synthesis.
/// </remarks>
public sealed class AgentFrameworkAgent : IAgent
{
    private readonly CapabilityToolProjector.Projection _capabilities;
    private readonly Lazy<AIAgent> _agent;
    private readonly AgentSessionRegistry _sessions;

    /// <summary>Initializes the agent with settings and capabilities.</summary>
    /// <remarks>Duplicate tool names and unsupported method declarations are validated during construction.</remarks>
    /// <param name="options">The endpoint, model, system instructions, and related settings.</param>
    /// <param name="capabilities">The capabilities exposed to the agent.</param>
    /// <exception cref="InvalidOperationException">A capability has an invalid tool declaration.</exception>
    public AgentFrameworkAgent(
        AgentFrameworkOptions options,
        IEnumerable<ICapability> capabilities)
        : this(options, capabilities, TimeProvider.System)
    {
    }

    /// <summary>Initializes the agent with a time provider for session management.</summary>
    /// <param name="options">The endpoint, model, system instructions, and related settings.</param>
    /// <param name="capabilities">The capabilities exposed to the agent.</param>
    /// <param name="timeProvider">The provider used for session last-used timestamps.</param>
    /// <exception cref="InvalidOperationException">A capability has an invalid tool declaration.</exception>
    public AgentFrameworkAgent(
        AgentFrameworkOptions options,
        IEnumerable<ICapability> capabilities,
        TimeProvider timeProvider)
        : this(options, capabilities, OpenAiCompatibleChatClientFactory.Create, timeProvider)
    {
    }

    internal AgentFrameworkAgent(
        AgentFrameworkOptions options,
        IEnumerable<ICapability> capabilities,
        Func<AgentFrameworkOptions, IChatClient> chatClientFactory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(chatClientFactory);

        _capabilities = CapabilityToolProjector.Project(capabilities);
        _agent = new Lazy<AIAgent>(
            () => Create(options, _capabilities, chatClientFactory),
            isThreadSafe: true);
        _sessions = new AgentSessionRegistry(
            async cancellationToken =>
                await _agent.Value.CreateSessionAsync(cancellationToken).ConfigureAwait(false),
            options,
            timeProvider ?? TimeProvider.System);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        AgentSession session;
        try
        {
            session = await _sessions
                .GetOrCreateAsync(request.SessionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Translate(exception);
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, request.UserText),
        };

        var updates = _agent.Value
            .RunStreamingAsync(messages, session, options: null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await updates.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw Translate(exception);
                }

                if (!moved)
                {
                    break;
                }

                foreach (var text in Spoken(updates.Current))
                {
                    yield return text;
                }
            }
        }
        finally
        {
            await updates.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Returns only spoken text from an update, excluding reasoning content and tool calls.</summary>
    private static IEnumerable<string> Spoken(AgentResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            if (content is TextContent { Text.Length: > 0 } text)
            {
                yield return text.Text;
            }
        }
    }

    private static ChatClientAgent Create(
        AgentFrameworkOptions options,
        CapabilityToolProjector.Projection capabilities,
        Func<AgentFrameworkOptions, IChatClient> chatClientFactory)
    {
        // Add the prefetch layer only when triggers exist. ChatClientAgent adds its tool-calling
        // layer outside this client, so prefetch belongs inside that layer.
        var chatClient = capabilities.Triggers.Count > 0
            ? new CapabilityPrefetchChatClient(
                chatClientFactory(options), capabilities.Triggers, options.OnPrefetchFailed)
            : chatClientFactory(options);

        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = options.Name,
            ChatHistoryProvider = History(options),
            ChatOptions = new ChatOptions
            {
                Instructions = options.Instructions,
                Tools = [.. capabilities.Tools],
                ModelId = options.Model,
                MaxOutputTokens = options.MaxOutputTokens,
            },
        });
    }

    /// <summary>Limits conversation history sent to the model to the configured number of recent messages.</summary>
    /// <remarks>
    /// <c>MessageCountingChatReducer</c> preserves system instructions and sends only the configured
    /// number of recent messages. This helps keep long conversations within the model's context window.
    /// </remarks>
    private static InMemoryChatHistoryProvider History(AgentFrameworkOptions options) =>
        new(new InMemoryChatHistoryProviderOptions
        {
#pragma warning disable MEAI001
            ChatReducer = new MessageCountingChatReducer(options.MaxHistoryMessages),
#pragma warning restore MEAI001
        });

    /// <summary>Converts framework and model exceptions to errors safe to return to clients.</summary>
    /// <remarks>Error messages do not include internal details such as endpoints or model names.</remarks>
    private static Exception Translate(Exception exception) => exception switch
    {
        ProviderException => exception,

        TimeoutException => new ProviderException(
            GatewayErrorCode.Timeout, "the model did not answer in time", retryable: true, exception),

        HttpRequestException => new ProviderException(
            GatewayErrorCode.Unavailable, "the model is unreachable", retryable: true, exception),

        ClientResultException => new ProviderException(
            GatewayErrorCode.Unavailable, "the model refused the request", retryable: true, exception),

        _ => new ProviderException(
            GatewayErrorCode.Internal, "unexpected gateway error", retryable: false, exception),
    };
}
