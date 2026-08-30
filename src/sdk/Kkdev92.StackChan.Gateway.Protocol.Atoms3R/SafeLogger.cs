using Microsoft.Extensions.Logging;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R;

/// <summary>
/// Prevents logging-provider exceptions from propagating into conversation processing.
/// </summary>
/// <remarks>
/// An active SSE response continues if logging fails because of storage exhaustion, a network
/// outage, or a similar condition. This protection applies only to AtomS3R conversation logs and
/// does not change host-wide logging behavior.
/// </remarks>
internal sealed class SafeLogger(ILogger inner) : ILogger
{
    /// <summary>
    /// Wraps a logger for the specified category with failure isolation.
    /// </summary>
    public static ILogger Create(ILoggerFactory factory, string name)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return new SafeLogger(factory.CreateLogger(name));
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        try
        {
            return inner.BeginScope(state);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        try
        {
            return inner.IsEnabled(logLevel);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
        catch (Exception thrown) when (thrown is not OperationCanceledException)
        {
            // Ignore logging exceptions because reporting them through the same logger would recurse.
        }
    }
}
