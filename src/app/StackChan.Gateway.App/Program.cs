using Kkdev92.StackChan.Gateway.AgentFramework;
using Kkdev92.StackChan.Gateway.Diagnostics;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Endpoints;
using Kkdev92.StackChan.Gateway.Runtime;
using StackChan.Capability.Time;
using StackChan.Capability.Weather;
using StackChan.Gateway.App;
using StackChan.Gateway.App.Diagnostics;
using StackChan.Gateway.App.Health;
using StackChan.Gateway.App.Security;
using StackChan.Provider.PiperPlus;
using StackChan.Provider.WhisperCpp;

// Use UTF-8 so log collectors can process redirected standard output on Windows.
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException)
{
    // The gateway can still start when the output target does not support encoding changes.
}

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Skip external-provider registration and configuration validation in offline mode.
var offline = configuration
    .GetSection(OfflineOptions.SectionName)
    .GetValue<bool>(nameof(OfflineOptions.Enabled));

builder.Services.AddStackChanRuntime(configuration);
builder.Services.AddStackChanAtoms3R(configuration);

// Limit connections from the unauthenticated health endpoint to external services.
builder.Services.AddSingleton<ProviderHealthEndpoint.ProbeCache>();
builder.Services.AddTimeCapability();

// Register weather support only when an API key is available so the gateway can start without it.
if (!string.IsNullOrWhiteSpace(
    configuration.GetSection(WeatherOptions.SectionName)[nameof(WeatherOptions.ApiKey)]))
{
    builder.Services.AddWeatherCapability(configuration);
}

if (offline)
{
    builder.Services.AddStackChanOfflineFixtures(configuration);

    // Reject unauthenticated LAN exposure in offline mode unless it is explicitly allowed.
    var risk = NetworkExposure.DescribeRisk(
        configuration[NetworkExposure.UrlsKey],
        hasToken: !string.IsNullOrEmpty(
            configuration.GetSection(Atoms3ROptions.SectionName)[nameof(Atoms3ROptions.Token)]),
        allowUnauthenticatedLan: configuration
            .GetValue<bool>(NetworkExposure.AllowUnauthenticatedLanKey));

    if (risk is not null)
    {
        throw new InvalidOperationException(risk);
    }
}
else
{
    builder.Services.AddWhisperCppSpeechToText(configuration);
    builder.Services.AddPiperPlusTextToSpeech(configuration);
    builder.Services.AddStackChanAgentFramework(configuration);

    // Require an authentication token when the configuration uses external providers.
    builder.Services.AddOptions<Atoms3ROptions>()
        .Validate(
            options => !string.IsNullOrEmpty(options.Token),
            "StackChan:Atoms3R:Token is required unless StackChan:Offline:Enabled is true.")
        .ValidateOnStart();
}

// Configure language- and character-specific default instructions in the app, not the general SDK.
builder.Services.PostConfigure<AgentFrameworkOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.Instructions))
    {
        options.Instructions = AppDefaults.Instructions;
    }
});

// Match Kestrel's request-body limit to the WAV limit accepted by the Atoms3R protocol.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize =
    configuration.GetSection(Atoms3ROptions.SectionName)
        .GetValue<long?>(nameof(Atoms3ROptions.MaxRequestBodyBytes)) ?? 2 * 1024 * 1024);

// Allow time to cancel turns that are sending audio before the host stops.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

// Log effective settings, including environment overrides, while excluding secret values.
StartupReport.Write(app.Services);

app.MapStackChanHealth();
app.MapStackChanAtoms3RConverse();

app.Run();

/// <summary>
/// Exposes the application entry point to integration tests.
/// </summary>
/// <remarks>
/// <c>WebApplicationFactory&lt;Program&gt;</c> requires a public <c>Program</c> type to start an
/// application that uses top-level statements.
/// </remarks>
public partial class Program;
