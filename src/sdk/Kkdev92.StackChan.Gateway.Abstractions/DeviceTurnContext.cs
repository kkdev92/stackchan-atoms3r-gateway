namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>
/// Represents device information that identifies the source of a turn.
/// </summary>
/// <remarks>
/// These values correlate requests with responses. <see cref="Abstractions.SessionId"/> identifies
/// the scope of conversation history.
/// </remarks>
/// <param name="DeviceId">Identifier of the source device.</param>
/// <param name="BootId">Device boot identifier, which changes when the device restarts.</param>
/// <param name="ConversationId">Conversation identifier assigned by the device.</param>
/// <exception cref="ArgumentException"><paramref name="DeviceId"/> is not set.</exception>
public sealed record DeviceTurnContext(
    DeviceId DeviceId,
    string BootId,
    string ConversationId)
{
    /// <summary>Gets the identifier of the source device.</summary>
    /// <remarks>
    /// <c>default(DeviceId)</c> is rejected as an unset value.
    /// </remarks>
    public DeviceId DeviceId { get; } = DeviceId.IsSet
        ? DeviceId
        : throw new ArgumentException(
            "Device id must not be default or empty.", nameof(DeviceId));
}
