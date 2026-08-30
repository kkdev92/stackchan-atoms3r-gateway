namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>Represents a capability that extends an agent.</summary>
/// <remarks>
/// A capability can be implemented as an ordinary .NET class without depending on a specific
/// agent implementation.
/// </remarks>
public interface ICapability
{
}

/// <summary>Marks a capability method that an agent can invoke.</summary>
/// <remarks>
/// The target must be a public instance method. Parameters must use JSON-serializable types. A
/// method may accept one <see cref="CancellationToken"/> as its final parameter. <see cref="Name"/>
/// must be unique across all capabilities registered by the application.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CapabilityActionAttribute : Attribute
{
    /// <summary>Creates the attribute with an invocation name and model-facing description.</summary>
    /// <param name="name">Invocation name that is unique across all capabilities.</param>
    /// <param name="description">Description used by the model to decide when to invoke the method.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="description"/> is <see langword="null"/>, empty,
    /// or consists only of white-space characters.
    /// </exception>
    public CapabilityActionAttribute(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name;
        Description = description;
    }

    /// <summary>Gets the capability invocation name.</summary>
    public string Name { get; }

    /// <summary>Gets the capability description presented to the model.</summary>
    public string Description { get; }

    /// <summary>Gets or sets whether the invocation leaves external state unchanged.</summary>
    /// <remarks>
    /// Set this to <see langword="true"/> to allow prefetch through <see cref="Triggers"/>. Prefetch
    /// runs before the model decides whether to call the method, so it is limited to read-only
    /// operations. The default is <see langword="false"/>.
    /// </remarks>
    public bool IsReadOnly { get; init; }

    /// <summary>Gets or sets phrases that trigger capability prefetch.</summary>
    /// <remarks>
    /// When a user utterance contains any configured phrase, the method runs before the model is
    /// called. This setting applies only when <see cref="IsReadOnly"/> is <see langword="true"/>.
    /// An empty array leaves invocation decisions to the model.
    /// </remarks>
    public string[] Triggers { get; init; } = [];
}
