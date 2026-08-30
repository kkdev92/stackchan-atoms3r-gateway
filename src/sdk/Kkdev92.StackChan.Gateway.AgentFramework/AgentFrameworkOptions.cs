namespace Kkdev92.StackChan.Gateway.AgentFramework;

/// <summary>Represents settings for an agent built with Microsoft Agent Framework.</summary>
/// <remarks>
/// Bind these settings from configuration files or environment variables so the endpoint and model
/// can vary by environment.
/// </remarks>
public sealed class AgentFrameworkOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "StackChan:Agent";

    /// <summary>Gets or sets an OpenAI-compatible endpoint, such as Foundry Local or LM Studio.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Gets or sets the model ID exposed by the endpoint.</summary>
    /// <remarks>Available IDs can usually be found at the endpoint's <c>/models</c> route.</remarks>
    public string Model { get; set; } = "";

    /// <summary>Gets or sets the API key sent to the OpenAI-compatible endpoint.</summary>
    public string ApiKey { get; set; } = "local";

    /// <summary>Gets or sets the agent name registered with Agent Framework.</summary>
    public string Name { get; set; } = "StackChan";

    /// <summary>Gets or sets the maximum number of output tokens allowed for one response.</summary>
    public int MaxOutputTokens { get; set; } = 512;

    /// <summary>Gets or sets the maximum number of history messages sent to the model.</summary>
    /// <remarks>
    /// System instructions do not count toward the limit. Without tool calls, each turn normally
    /// adds a user and an assistant message, so the default of 10 retains approximately five turns.
    /// Limiting history helps keep requests within the model's context window.
    /// </remarks>
    public int MaxHistoryMessages { get; set; } = 10;

    /// <summary>Gets or sets the system instructions passed to the agent.</summary>
    /// <remarks>
    /// The SDK does not provide default instructions because it does not prescribe a language or
    /// persona. <c>AddStackChanAgentFramework</c> rejects an empty value at startup.
    /// </remarks>
    public string Instructions { get; set; } = "";

    /// <summary>Gets or sets the maximum number of agent sessions retained in memory.</summary>
    /// <remarks>
    /// Because each session retains conversation history, this limit prevents memory usage from
    /// growing without bound as previously unseen session IDs arrive.
    /// </remarks>
    public int MaxSessions { get; set; } = 128;

    /// <summary>Gets or sets the time, in minutes, before an inactive session is discarded.</summary>
    /// <remarks>
    /// The next turn for a discarded session starts a new conversation.
    /// </remarks>
    public int SessionIdleTimeoutMinutes { get; set; } = 120;

    /// <summary>Gets or sets a callback invoked when a prefetched capability fails.</summary>
    /// <remarks>
    /// A prefetch failure does not interrupt the conversation; normal model response generation
    /// continues. The callback receives the capability name and the exception.
    /// </remarks>
    public Action<string, Exception>? OnPrefetchFailed { get; set; }
}
