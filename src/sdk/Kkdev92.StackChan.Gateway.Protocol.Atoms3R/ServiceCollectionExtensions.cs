using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R;

/// <summary>
/// Provides dependency injection extensions for the AtomS3R protocol.
/// </summary>
/// <remarks>
/// Register the conversation endpoint separately with
/// <see cref="Endpoints.ConverseEndpoint.MapStackChanAtoms3RConverse"/>.
/// </remarks>
public static class Atoms3RServiceCollectionExtensions
{
    /// <summary>
    /// Reads configuration and registers AtomS3R protocol options.
    /// </summary>
    /// <remarks>
    /// An empty token is valid and disables authentication. A host that requires authentication
    /// adds its own option validation. The keep-alive interval is capped at 25 seconds, below the
    /// device timeout of 30 seconds.
    /// </remarks>
    /// <param name="services">Service collection to update.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns><paramref name="services"/> for method chaining.</returns>
    public static IServiceCollection AddStackChanAtoms3R(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<Atoms3ROptions>()
            .Bind(configuration.GetSection(Atoms3ROptions.SectionName))
            .Validate(
                options => options.MaxRequestBodyBytes > 0,
                "StackChan:Atoms3R:MaxRequestBodyBytes must be positive.")
            .Validate(
                options => options.MaxSpokenTextBytes > 0 &&
                    options.MaxSpokenTextBytes <= options.MaxRequestBodyBytes,
                "StackChan:Atoms3R:MaxSpokenTextBytes must be positive and " +
                "not larger than MaxRequestBodyBytes.")
            .Validate(
                options => options.KeepAliveIntervalSeconds is > 0 and <= 25,
                "StackChan:Atoms3R:KeepAliveIntervalSeconds must be between 1 and 25.")
            .ValidateOnStart();

        return services;
    }
}
