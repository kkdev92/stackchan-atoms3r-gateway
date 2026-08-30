using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Models;

/// <summary>
/// Creates a chat client connected to an OpenAI-compatible endpoint.
/// </summary>
/// <remarks>
/// <para>
/// This factory makes endpoints such as Foundry Local and LM Studio available as
/// <see cref="IChatClient"/> implementations.
/// </para>
/// <para>
/// The client layers are arranged in the following order.
/// </para>
/// <code>
/// Model
///  ↑ TextToolCallChatClient       (structures tool calls embedded in response text)
///  ↑ CapabilityPrefetchChatClient (prefetches capabilities only when triggers are present)
///  ↑ FunctionInvokingChatClient   (added by ChatClientAgent)
///  ↑ Agent
/// </code>
/// <para>
/// <c>ChatClientAgent</c> adds <c>FunctionInvokingChatClient</c>. This factory intentionally does not
/// add another instance, which would execute tools twice.
/// </para>
/// </remarks>
internal static class OpenAiCompatibleChatClientFactory
{
    /// <summary>Creates an <see cref="IChatClient"/> from the supplied settings.</summary>
    /// <param name="options">The endpoint and model settings.</param>
    public static IChatClient Create(AgentFrameworkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.Endpoint) });

        // Measure next to the model so tool-call iteration time is excluded.
        IChatClient inner = new MeasuredChatClient(
            client.GetChatClient(options.Model).AsIChatClient());

        return new TextToolCallChatClient(inner);
    }
}
