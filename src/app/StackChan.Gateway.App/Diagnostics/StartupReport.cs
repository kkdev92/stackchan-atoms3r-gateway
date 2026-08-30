using System.Globalization;
using Kkdev92.StackChan.Gateway.AgentFramework;
using Kkdev92.StackChan.Gateway.Diagnostics;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R;
using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Microsoft.Extensions.Options;
using StackChan.Capability.Weather;
using StackChan.Provider.PiperPlus;
using StackChan.Provider.WhisperCpp;

namespace StackChan.Gateway.App.Diagnostics;

/// <summary>Records effective application settings at startup.</summary>
/// <remarks>
/// <para>
/// Logging limits overridden by environment variables or configuration files helps diagnose request
/// rejection and timeout causes.
/// </para>
/// <para>
/// Settings are written only to logs and are not exposed over the network. API keys and tokens are
/// represented only by their presence and length, never their value. Length can help diagnose
/// incomplete input.
/// </para>
/// </remarks>
internal static class StartupReport
{
    public static void Write(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("StackChan.Config");

        var offline = Value<OfflineOptions>(services);
        var runtime = Value<TurnRuntimeOptions>(services);
        var wire = Value<Atoms3ROptions>(services);

        Line(logger, "StackChan:Offline", [("Enabled", offline.Enabled)]);

        Line(logger, "StackChan:Runtime",
        [
            ("MaxConcurrentTurns", runtime.MaxConcurrentTurns),
            ("TurnTimeoutSeconds", runtime.TurnTimeoutSeconds),
            ("MaxSessions", runtime.MaxSessions),
            ("SessionIdleTimeoutMinutes", runtime.SessionIdleTimeoutMinutes),
        ]);

        Line(logger, "StackChan:Atoms3R",
        [
            ("Token", Secret(wire.Token)),
            ("MaxRequestBodyBytes", wire.MaxRequestBodyBytes),
            ("MaxSpokenTextBytes", wire.MaxSpokenTextBytes),
            ("KeepAliveIntervalSeconds", wire.KeepAliveIntervalSeconds),
        ]);

        if (offline.Enabled)
        {
            // Do not resolve unregistered external-provider settings in offline mode.
            return;
        }

        var stt = Value<WhisperCppOptions>(services);
        var tts = Value<PiperPlusOptions>(services);
        var agent = Value<AgentFrameworkOptions>(services);

        Line(logger, "StackChan:WhisperCpp",
        [
            ("Endpoint", stt.Endpoint),
            ("TimeoutSeconds", stt.TimeoutSeconds),
            ("MaxResponseBytes", stt.MaxResponseBytes),
            ("MinLanguageProbability", stt.MinLanguageProbability),
        ]);

        Line(logger, "StackChan:PiperPlus",
        [
            ("Endpoint", tts.Endpoint),
            ("TimeoutSeconds", tts.TimeoutSeconds),
            ("MaxResponseBytes", tts.MaxResponseBytes),
            ("LengthScale", tts.LengthScale),
        ]);

        Line(logger, "StackChan:Agent",
        [
            ("Endpoint", agent.Endpoint),
            ("Model", agent.Model),
            ("ApiKey", Secret(agent.ApiKey)),
            ("MaxOutputTokens", agent.MaxOutputTokens),
            ("MaxHistoryMessages", agent.MaxHistoryMessages),
            ("MaxSessions", agent.MaxSessions),
            ("SessionIdleTimeoutMinutes", agent.SessionIdleTimeoutMinutes),
            // Record only the instruction length, which confirms binding without exposing content.
            ("InstructionsChars", agent.Instructions.Length),
        ]);

        // Omit this section when no API key is present and weather support is not registered.
        if (services.GetService<IOptions<WeatherOptions>>()?.Value is { ApiKey.Length: > 0 } weather)
        {
            Line(logger, "StackChan:Weather",
            [
                ("Endpoint", weather.Endpoint),
                ("ApiKey", Secret(weather.ApiKey)),
                ("DefaultLocation", weather.DefaultLocation),
                ("TimeoutSeconds", weather.TimeoutSeconds),
            ]);
        }
    }

    // Keep related values on one line so logs can be searched by configuration section.
    private static void Line(
        ILogger logger,
        string section,
        IReadOnlyList<(string Key, object Value)> values)
    {
        var text = string.Join(
            " ",
            values.Select(pair => $"{pair.Key}={Show(pair.Value)}"));

        logger.LogInformation("config section={Section} {Values}", section, text);
    }

    private static string Secret(string? value) =>
        string.IsNullOrEmpty(value)
            ? "absent"
            : $"set({value.Length.ToString(CultureInfo.InvariantCulture)})";

    private static string Show(object value) => value switch
    {
        // Represent an empty string with quotes to distinguish it from an absent value.
        string text => text.Length == 0 ? "\"\"" : text,
        bool flag => flag ? "true" : "false",
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static TOptions Value<TOptions>(IServiceProvider services)
        where TOptions : class =>
        services.GetRequiredService<IOptions<TOptions>>().Value;
}
