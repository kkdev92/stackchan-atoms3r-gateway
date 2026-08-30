using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Sessions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Runtime.Sessions;
using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kkdev92.StackChan.Gateway.Runtime;

/// <summary>
/// Provides extension methods for registering the turn runtime with a dependency injection container.
/// </summary>
/// <remarks>
/// These methods do not register speech recognition, agent, or speech synthesis implementations.
/// The application must register the providers it uses.
/// </remarks>
public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the turn runtime, session registry, and related options.
    /// </summary>
    /// <remarks>
    /// Services are registered with <c>TryAdd</c>. To customize session management or the runtime,
    /// register the same service before calling this method or replace it afterward.
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns><paramref name="services"/>, so that additional calls can be chained.</returns>
    public static IServiceCollection AddStackChanRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TurnRuntimeOptions>()
            .Bind(configuration.GetSection(TurnRuntimeOptions.SectionName))
            .Validate(
                options => options.MaxConcurrentTurns >= 1,
                "StackChan:Runtime:MaxConcurrentTurns must be at least 1.")
            .Validate(
                // Also validate the upper bound so a misconfiguration cannot occupy a slot for too long.
                options => options.TurnTimeoutSeconds is >= 1 and <= 3600,
                "StackChan:Runtime:TurnTimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.MaxSessions >= 1,
                "StackChan:Runtime:MaxSessions must be at least 1.")
            .Validate(
                options => options.SessionIdleTimeoutMinutes >= 1,
                "StackChan:Runtime:SessionIdleTimeoutMinutes must be at least 1.")
            .Configure<ILoggerFactory>((options, loggers) =>
            {
                var logger = loggers.CreateLogger("StackChan.Turn");

                options.OnUnexpected = exception =>
                {
                    try
                    {
                        logger.LogError(exception, "turn stage={Stage}", "unexpected");
                    }
                    catch (Exception thrown) when (thrown is not OperationCanceledException)
                    {
                        // Do not let a logging failure replace the original exception handling.
                    }
                };
            })
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<ISessionRegistry>(provider => new InMemorySessionRegistry(
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<TurnRuntimeOptions>>().Value));

        // Prefer a caller-provided ITurnRuntime registered before this method is called.
        services.TryAddSingleton<ITurnRuntime>(provider => new ObservedTurnRuntime(
            new TurnRuntime(
                provider.GetRequiredService<ISessionRegistry>(),
                provider.GetRequiredService<ISpeechToText>(),
                provider.GetRequiredService<IAgent>(),
                provider.GetRequiredService<ITextToSpeech>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IOptions<TurnRuntimeOptions>>().Value),
            provider.GetRequiredService<TimeProvider>()));

        return services;
    }
}
