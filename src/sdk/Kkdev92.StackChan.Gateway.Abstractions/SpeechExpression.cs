namespace Kkdev92.StackChan.Gateway.Abstractions;

/// <summary>
/// Represents the expression shown by the device while speaking.
/// </summary>
/// <remarks>
/// The protocol implementation converts this value to protocol-specific labels such as <c>[happy]</c>.
/// </remarks>
public enum SpeechExpression
{
    /// <summary>The default expression.</summary>
    Neutral,

    /// <summary>An expression of happiness.</summary>
    Happy,

    /// <summary>An expression of sadness.</summary>
    Sad,

    /// <summary>An expression of doubt or uncertainty.</summary>
    Doubt,

    /// <summary>An expression of sleepiness.</summary>
    Sleepy,

    /// <summary>An expression of anger.</summary>
    Angry,
}
