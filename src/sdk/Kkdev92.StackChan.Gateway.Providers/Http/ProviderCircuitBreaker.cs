using System.Diagnostics;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Telemetry;

namespace Kkdev92.StackChan.Gateway.Providers.Http;

/// <summary>Temporarily stops calls to a provider after repeated failures.</summary>
/// <remarks>
/// <para>
/// The breaker opens after the configured number of consecutive retryable
/// <see cref="ProviderException"/> instances or unclassified exceptions. While open, it rejects
/// requests without calling the provider. After the open period, one probe is allowed; a successful
/// probe closes the breaker.
/// </para>
/// <para>
/// Non-retryable provider failures and caller cancellation do not count toward the threshold. This
/// class does not retry failed work itself.
/// </para>
/// </remarks>
public sealed class ProviderCircuitBreaker
{
    private readonly string _name;

    private readonly int _threshold;

    private readonly TimeSpan _openFor;

    private readonly TimeProvider _time;

    private readonly Lock _gate = new();

    private int _failures;

    private DateTimeOffset? _openUntil;

    private bool _probing;

    /// <summary>Creates a provider circuit breaker.</summary>
    /// <param name="name">
    /// Provider name. This becomes a metric attribute, so use a bounded value such as <c>stt</c>,
    /// <c>tts</c>, or <c>model</c> and do not include URLs or device identifiers.
    /// </param>
    /// <param name="threshold">Consecutive failures allowed before opening; defaults to <c>3</c>.</param>
    /// <param name="openFor">
    /// Duration for which calls stop; defaults to 15 seconds. Specify a positive value.
    /// </param>
    /// <param name="timeProvider">Time source, or <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threshold"/> is less than 1.</exception>
    public ProviderCircuitBreaker(
        string name,
        int threshold = 3,
        TimeSpan? openFor = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);

        _name = name;
        _threshold = threshold;
        _openFor = openFor ?? TimeSpan.FromSeconds(15);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets whether the breaker is within its open period.</summary>
    /// <remarks>
    /// This is <see langword="false"/> while the recovery probe is running, although other calls
    /// remain rejected.
    /// </remarks>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _openUntil is { } until && _time.GetUtcNow() < until;
            }
        }
    }

    /// <summary>Runs work once through the circuit breaker.</summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="work">Work that calls the provider.</param>
    /// <param name="whenOpen">Message reported to the device when the breaker is open.</param>
    /// <param name="cancellationToken">Token that signals cancellation by the caller.</param>
    /// <returns>The provider result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work"/> is <see langword="null"/>.</exception>
    /// <exception cref="ProviderException">
    /// The breaker rejected the call, or <paramref name="work"/> threw <see cref="ProviderException"/>.
    /// </exception>
    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> work,
        string whenOpen,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var probe = Enter();

        if (probe == Admission.Rejected)
        {
            // Do not call the provider while the breaker is open.
            GatewayTelemetry.ProviderCalled(_name, "rejected", TimeSpan.Zero);

            throw ProviderEndpoint.Unavailable(whenOpen);
        }

        // Measure provider calls that pass through this breaker.
        var began = Stopwatch.GetTimestamp();

        try
        {
            var result = await work(cancellationToken).ConfigureAwait(false);

            Succeeded();
            GatewayTelemetry.ProviderCalled(_name, "ok", Stopwatch.GetElapsedTime(began));

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is not a provider failure.
            Released();
            GatewayTelemetry.ProviderCalled(_name, "cancelled", Stopwatch.GetElapsedTime(began));
            throw;
        }
        catch (ProviderException exception)
        {
            // Count only failures that may recover on retry.
            if (exception.Retryable)
            {
                Failed();
            }
            else
            {
                Released();
            }

            GatewayTelemetry.ProviderCalled(
                _name,
                exception.Code == GatewayErrorCode.Timeout ? "timeout" : "failed",
                Stopwatch.GetElapsedTime(began));

            throw;
        }
        catch
        {
            // Count exceptions not converted to ProviderException as provider failures.
            Failed();
            GatewayTelemetry.ProviderCalled(_name, "failed", Stopwatch.GetElapsedTime(began));
            throw;
        }
    }

    private enum Admission
    {
        Closed,

        Probing,

        Rejected,
    }

    private Admission Enter()
    {
        lock (_gate)
        {
            if (_openUntil is not { } until)
            {
                return Admission.Closed;
            }

            if (_time.GetUtcNow() < until)
            {
                return Admission.Rejected;
            }

            // Allow only one recovery probe after the open period.
            if (_probing)
            {
                return Admission.Rejected;
            }

            _probing = true;

            return Admission.Probing;
        }
    }

    private void Succeeded()
    {
        lock (_gate)
        {
            _failures = 0;
            _openUntil = null;
            _probing = false;
        }
    }

    private void Failed()
    {
        lock (_gate)
        {
            _probing = false;

            // A failed recovery probe reopens the breaker without restarting the failure count.
            if (_openUntil is not null)
            {
                _openUntil = _time.GetUtcNow() + _openFor;
                GatewayTelemetry.BreakerOpenedFor(_name);

                return;
            }

            _failures++;

            if (_failures >= _threshold)
            {
                _openUntil = _time.GetUtcNow() + _openFor;
                GatewayTelemetry.BreakerOpenedFor(_name);
            }
        }
    }

    private void Released()
    {
        lock (_gate)
        {
            _probing = false;
        }
    }
}
