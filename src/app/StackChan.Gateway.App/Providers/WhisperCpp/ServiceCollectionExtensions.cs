using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Providers.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace StackChan.Provider.WhisperCpp;

/// <summary>
/// Registers whisper.cpp speech recognition with a dependency injection container.
/// </summary>
public static class WhisperCppServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WhisperCppSpeechToText"/> and its dependencies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="HttpClient.Timeout"/> is disabled because <see cref="WhisperCppSpeechToText"/>
    /// manages recognition deadlines through cancellation tokens.
    /// </para>
    /// <para>
    /// The singleton provider obtains a client for each request instead of retaining one, allowing
    /// <see cref="IHttpClientFactory"/> to rotate handlers normally.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><paramref name="services"/>, so that additional calls can be chained.</returns>
    public static IServiceCollection AddWhisperCppSpeechToText(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<WhisperCppOptions>()
            .Bind(configuration.GetSection(WhisperCppOptions.SectionName))
            .Validate(
                options => ProviderEndpoint.IsAbsoluteHttp(options.Endpoint),
                "StackChan:WhisperCpp:Endpoint must be an absolute http(s) URI.")
            .Validate(
                options => options.TimeoutSeconds > 0,
                "StackChan:WhisperCpp:TimeoutSeconds must be positive.")
            .Validate(
                options => options.MaxResponseBytes > 0,
                "StackChan:WhisperCpp:MaxResponseBytes must be positive.")
            // Allow 0 as the value that disables the check.
            .Validate(
                options => options.MinLanguageProbability is >= 0 and <= 1,
                "StackChan:WhisperCpp:MinLanguageProbability must be between 0 and 1.")
            .ValidateOnStart();

        services.AddHttpClient(
            WhisperCppSpeechToText.HttpClientName,
            http => http.Timeout = Timeout.InfiniteTimeSpan);

        services.TryAddSingleton<ISpeechToText>(provider => new WhisperCppSpeechToText(
            () => provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(WhisperCppSpeechToText.HttpClientName),
            provider.GetRequiredService<IOptions<WhisperCppOptions>>().Value));

        return services;
    }

}
