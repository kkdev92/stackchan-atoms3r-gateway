using Kkdev92.StackChan.Gateway.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kkdev92.StackChan.Gateway.Diagnostics;

/// <summary>
/// Registers offline diagnostic components with a dependency injection container.
/// </summary>
public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers fixed implementations of speech recognition, the agent, and synthesis.
    /// </summary>
    /// <remarks>
    /// This method does not inspect <see cref="OfflineOptions.Enabled"/> and always registers the
    /// fixed implementations when called. The host chooses between this method and its real
    /// provider registrations. Existing registrations are preserved.
    /// </remarks>
    /// <param name="services">Service collection to update.</param>
    /// <param name="configuration">Application configuration used to read <see cref="OfflineOptions"/>.</param>
    /// <returns><paramref name="services"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddStackChanOfflineFixtures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Apply defaults only after the configuration binder has finished appending array values.
        services.AddOptions<OfflineOptions>()
            .Bind(configuration.GetSection(OfflineOptions.SectionName))
            .PostConfigure(options => options.ApplyDefaults())
            .ValidateOnStart();

        services.TryAddSingleton<ISpeechToText>(provider => new FixedTranscriptSpeechToText(
            provider.GetRequiredService<IOptions<OfflineOptions>>().Value));

        services.TryAddSingleton<IAgent>(provider => new FixedResponseAgent(
            provider.GetRequiredService<IOptions<OfflineOptions>>().Value));

        services.TryAddSingleton<ITextToSpeech, ToneTextToSpeech>();

        return services;
    }
}
