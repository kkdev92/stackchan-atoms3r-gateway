namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>
/// Represents an identifier for a StackChan device.
/// </summary>
/// <remarks>
/// In the AtomS3R protocol, this corresponds to the <c>X-StackChan-Device</c> header value.
/// </remarks>
public readonly record struct DeviceId
{
    /// <summary>Creates a device identifier from a string.</summary>
    /// <param name="value">Non-white-space string that uniquely identifies a device.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is <see langword="null"/>, empty, or consists only of white space.
    /// </exception>
    public DeviceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Device id must not be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the string representation of the device identifier.</summary>
    /// <remarks>
    /// This is <see langword="null"/> for <c>default(DeviceId)</c>. Check <see cref="IsSet"/>
    /// before using the value.
    /// </remarks>
    public string Value { get; }

    /// <summary>
    /// Gets whether a valid device identifier is set.
    /// </summary>
    public bool IsSet => !string.IsNullOrWhiteSpace(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Represents a session identifier that associates multiple turns with one conversation.
/// </summary>
/// <remarks>
/// This identifier is independent of any session object used internally by an agent implementation.
/// </remarks>
public readonly record struct SessionId
{
    /// <summary>Creates a session identifier from a string.</summary>
    /// <param name="value">Non-white-space string that identifies a conversation.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is <see langword="null"/>, empty, or consists only of white space.
    /// </exception>
    public SessionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Session id must not be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the string representation of the session identifier.</summary>
    /// <remarks>
    /// This is <see langword="null"/> for <c>default(SessionId)</c>. Check <see cref="IsSet"/>
    /// before using the value.
    /// </remarks>
    public string Value { get; }

    /// <summary>
    /// Gets whether a valid session identifier is set.
    /// </summary>
    public bool IsSet => !string.IsNullOrWhiteSpace(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
