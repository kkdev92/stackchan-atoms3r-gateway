namespace Kkdev92.StackChan.Gateway.Abstractions.Sessions;

/// <summary>
/// Represents session state as a read-only value.
/// </summary>
/// <param name="SessionId">Session identifier.</param>
/// <param name="DeviceId">Device identifier associated with the session.</param>
/// <param name="CreatedAt">Time when the session was created.</param>
/// <param name="LastActivityAt">Time when the session was last used.</param>
public sealed record SessionSnapshot(
    SessionId SessionId,
    DeviceId DeviceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt);
