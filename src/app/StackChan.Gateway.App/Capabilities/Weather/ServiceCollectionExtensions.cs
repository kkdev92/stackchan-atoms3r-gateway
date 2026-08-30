using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Capabilities;
using Microsoft.Extensions.Options;

namespace StackChan.Capability.Weather;

/// <summary>
/// Registers the weather capability with a dependency injection container.
/// </summary>
public static class WeatherCapabilityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WeatherCapability"/> and its dependencies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Required settings, including the API key, are validated at startup. Pass the key through the
    /// <c>StackChan__Weather__ApiKey</c> environment variable.
    /// </para>
    /// <para>
    /// <see cref="WeatherCapability"/> manages request deadlines through cancellation tokens. A client
    /// is obtained for each request so <see cref="IHttpClientFactory"/> can rotate handlers normally.
    /// </para>
    /// <para>
    /// Multiple <see cref="ICapability"/> services can be registered, so existing capabilities are not replaced.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><paramref name="services"/>, so that additional calls can be chained.</returns>
    public static IServiceCollection AddWeatherCapability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<WeatherOptions>()
            .Bind(configuration.GetSection(WeatherOptions.SectionName))
            .Validate(
                options => CapabilityEndpoint.IsAbsoluteHttp(options.Endpoint),
                "StackChan:Weather:Endpoint must be an absolute http(s) URI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "StackChan:Weather:ApiKey is required. Pass it as the StackChan__Weather__ApiKey environment variable.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DefaultLocation),
                "StackChan:Weather:DefaultLocation must not be empty.")
            .Validate(
                options => options.TimeoutSeconds > 0,
                "StackChan:Weather:TimeoutSeconds must be positive.")
            .ValidateOnStart();

        // Use a dedicated HTTP logger so the query string containing the API key is never logged.
        services.AddSingleton<QueryFreeHttpLogger>();
        services.AddHttpClient(
                WeatherCapability.HttpClientName,
                http => http.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers()
            .AddLogger<QueryFreeHttpLogger>();

        services.AddSingleton<ICapability>(provider =>
        {
            var clients = provider.GetRequiredService<IHttpClientFactory>();

            return new WeatherCapability(
                () => clients.CreateClient(WeatherCapability.HttpClientName),
                provider.GetRequiredService<IOptions<WeatherOptions>>().Value,
                provider.GetService<ILogger<WeatherCapability>>());
        });

        return services;
    }

}
