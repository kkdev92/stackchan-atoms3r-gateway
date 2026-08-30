namespace Kkdev92.StackChan.Gateway.Runtime.Concurrency;

/// <summary>
/// Limits the number of turns that can run concurrently.
/// </summary>
/// <remarks>
/// When no slot is available, the gate rejects the request immediately instead of queuing it
/// until the device's initial-response timeout. This allows the caller to retry.
/// </remarks>
public sealed class TurnConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim _slots;

    /// <summary>Initializes the gate with a concurrency limit.</summary>
    /// <param name="maxConcurrentTurns">The maximum number of concurrent turns. Must be at least 1.</param>
    public TurnConcurrencyGate(int maxConcurrentTurns)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentTurns);

        _slots = new SemaphoreSlim(maxConcurrentTurns, maxConcurrentTurns);
    }

    /// <summary>Acquires a slot if one is available; otherwise returns <see langword="false"/> immediately.</summary>
    public bool TryEnter() => _slots.Wait(TimeSpan.Zero);

    /// <summary>Releases an acquired slot.</summary>
    public void Leave() => _slots.Release();

    /// <summary>Releases the resources held by the gate.</summary>
    public void Dispose() => _slots.Dispose();
}
