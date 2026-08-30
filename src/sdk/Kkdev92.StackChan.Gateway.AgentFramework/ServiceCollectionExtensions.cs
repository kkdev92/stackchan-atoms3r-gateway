using Kkdev92.StackChan.Gateway.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kkdev92.StackChan.Gateway.AgentFramework;

/// <summary>
/// Provides extension methods for registering the Agent Framework agent with a dependency injection container.
/// </summary>
public static class AgentFrameworkServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Microsoft Agent Framework agent and related options.
    /// </summary>
    /// <remarks>
    /// Required settings are validated at startup. Registered capabilities are also projected to
    /// tools at startup, so duplicate tool names and unsupported method declarations are detected
    /// before the first conversation. To use another <see cref="IAgent"/> implementation, such as a
    /// fixed-response agent, register it directly instead of calling this method.
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><paramref name="services"/>, so that additional calls can be chained.</returns>
    public static IServiceCollection AddStackChanAgentFramework(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AgentFrameworkOptions>()
            .Bind(configuration.GetSection(AgentFrameworkOptions.SectionName))
            .Validate(
                options => IsAbsoluteHttpUri(options.Endpoint),
                "StackChan:Agent:Endpoint must be an absolute http(s) URI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Model),
                "StackChan:Agent:Model is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Name),
                "StackChan:Agent:Name must not be empty.")
            .Validate(
                // Validate the upper bound so a misconfiguration cannot generate extremely long responses.
                options => options.MaxOutputTokens is > 0 and <= 8192,
                "StackChan:Agent:MaxOutputTokens must be between 1 and 8192.")
            // Require at least two messages so the most recent user message and response can be retained.
            .Validate(
                options => options.MaxHistoryMessages >= 2,
                "StackChan:Agent:MaxHistoryMessages must be at least 2.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Instructions),
                "StackChan:Agent:Instructions is required. Set it in configuration (the official app ships a default).")
            .Validate(
                options => options.MaxSessions >= 1,
                "StackChan:Agent:MaxSessions must be at least 1.")
            .Validate(
                options => options.SessionIdleTimeoutMinutes >= 1,
                "StackChan:Agent:SessionIdleTimeoutMinutes must be at least 1.")
            .Configure<ILoggerFactory>((options, loggers) =>
            {
                var logger = loggers.CreateLogger("StackChan.Capability");

                options.OnPrefetchFailed = (name, exception) => logger.LogWarning(
                    exception,
                    "capability name={Name} stage={Stage}",
                    name,
                    "prefetch-failed");
            })
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IAgent>(provider => new AgentFrameworkAgent(
            provider.GetRequiredService<IOptions<AgentFrameworkOptions>>().Value,
            provider.GetServices<ICapability>(),
            provider.GetRequiredService<TimeProvider>()));

        services.AddHostedService<AgentStartupValidator>();

        return services;
    }

    private static bool IsAbsoluteHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>
/// Resolves the agent at startup to validate configuration and tool declarations.
/// </summary>
/// <remarks>
/// Capabilities are projected to tools when the agent is constructed, so resolving the agent detects
/// duplicate tool names and unsupported method declarations.
/// </remarks>
/// <param name="agent">The agent to validate.</param>
internal sealed class AgentStartupValidator(IAgent agent) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = agent;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
