using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Providers.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace StackChan.Provider.PiperPlus;

/// <summary>
/// Registers piper-plus speech synthesis with a dependency injection container.
/// </summary>
public static class PiperPlusServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PiperPlusTextToSpeech"/> and its dependencies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="HttpClient.Timeout"/> is disabled because <see cref="PiperPlusTextToSpeech"/>
    /// manages synthesis deadlines through cancellation tokens.
    /// </para>
    /// <para>
    /// The singleton provider obtains a client for each request instead of retaining one, allowing
    /// <see cref="IHttpClientFactory"/> to rotate handlers normally.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><paramref name="services"/>, so that additional calls can be chained.</returns>
    public static IServiceCollection AddPiperPlusTextToSpeech(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PiperPlusOptions>()
            .Bind(configuration.GetSection(PiperPlusOptions.SectionName))
            .Validate(
                options => ProviderEndpoint.IsAbsoluteHttp(options.Endpoint),
                "StackChan:PiperPlus:Endpoint must be an absolute http(s) URI.")
            .Validate(
                options => options.LengthScale > 0,
                "StackChan:PiperPlus:LengthScale must be positive.")
            .Validate(
                options => options.TimeoutSeconds > 0,
                "StackChan:PiperPlus:TimeoutSeconds must be positive.")
            .Validate(
                options => options.MaxResponseBytes > 0,
                "StackChan:PiperPlus:MaxResponseBytes must be positive.")
            .ValidateOnStart();

        services.AddHttpClient(
            PiperPlusTextToSpeech.HttpClientName,
            http => http.Timeout = Timeout.InfiniteTimeSpan);

        services.TryAddSingleton<ITextToSpeech>(provider => new PiperPlusTextToSpeech(
            () => provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(PiperPlusTextToSpeech.HttpClientName),
            provider.GetRequiredService<IOptions<PiperPlusOptions>>().Value));

        return services;
    }

}
