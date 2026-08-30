namespace Kkdev92.StackChan.Gateway.Runtime.Turns;

/// <summary>Configures execution limits and session management for the turn runtime.</summary>
public sealed class TurnRuntimeOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "StackChan:Runtime";

    /// <summary>Gets or sets the maximum number of turns that can run concurrently.</summary>
    public int MaxConcurrentTurns { get; set; } = 2;

    /// <summary>Gets or sets the timeout for a turn, in seconds.</summary>
    /// <remarks>
    /// The runtime limits the total execution time so that a turn cannot run indefinitely,
    /// even when keep-alive events keep the connection open.
    /// </remarks>
    public int TurnTimeoutSeconds { get; set; } = 120;

    /// <summary>Gets or sets the maximum number of sessions retained in memory.</summary>
    /// <remarks>
    /// This limit prevents sessions and locks from growing without bound when requests contain
    /// previously unseen device IDs.
    /// </remarks>
    public int MaxSessions { get; set; } = 128;

    /// <summary>Gets or sets the time, in minutes, before an inactive session is discarded.</summary>
    /// <remarks>
    /// The next turn for a discarded session starts a new conversation.
    /// </remarks>
    public int SessionIdleTimeoutMinutes { get; set; } = 120;

    /// <summary>Gets or sets a callback that receives unexpected exceptions.</summary>
    /// <remarks>
    /// The client receives only a safe, predefined message. The original exception is passed to
    /// this callback for diagnostics. This property cannot be bound from a configuration file.
    /// </remarks>
    public Action<Exception>? OnUnexpected { get; set; }
}
