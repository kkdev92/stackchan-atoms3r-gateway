using Kkdev92.StackChan.Gateway.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StackChan.Capability.Time;

/// <summary>
/// Registers the current-time capability with a dependency injection container.
/// </summary>
public static class TimeCapabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TimeCapability"/> and its dependencies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddSingleton</c> is used because multiple <see cref="ICapability"/> services can be registered.
    /// </para>
    /// <para>
    /// The system clock is added only when no <see cref="TimeProvider"/> is registered. A provider
    /// registered by the caller is preserved, allowing tests and custom time sources.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <returns><paramref name="services"/>, so that additional calls can be chained.</returns>
    public static IServiceCollection AddTimeCapability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<ICapability>(provider =>
            new TimeCapability(provider.GetRequiredService<TimeProvider>()));

        return services;
    }
}
