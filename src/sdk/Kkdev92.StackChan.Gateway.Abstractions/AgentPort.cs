namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>Represents one user input passed to an agent.</summary>
/// <param name="SessionId">Session identifier used to associate conversation history.</param>
/// <param name="DeviceId">Identifier of the device that supplied the input.</param>
/// <param name="UserText">Utterance obtained from speech recognition or text input.</param>
public sealed record AgentRequest(
    SessionId SessionId,
    DeviceId DeviceId,
    string UserText);

/// <summary>
/// Generates responses to user input.
/// </summary>
/// <remarks>
/// The stream contains only text intended for the device to speak. Implementations remove tool
/// calls, reasoning content, token usage, and other internal information.
/// </remarks>
public interface IAgent
{
    /// <summary>Returns response text in generation order.</summary>
    /// <param name="request">User input passed to the agent.</param>
    /// <param name="cancellationToken">Token that signals cancellation of response generation.</param>
    /// <returns>An asynchronous stream of text intended to be spoken.</returns>
    IAsyncEnumerable<string> StreamAsync(
        AgentRequest request,
        CancellationToken cancellationToken);
}
