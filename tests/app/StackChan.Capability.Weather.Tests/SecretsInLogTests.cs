using Kkdev92.StackChan.Gateway.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace StackChan.Capability.Weather.Tests;

/// <summary>
/// Verifies that the weather API HTTP logger does not expose secrets.
/// </summary>
/// <remarks>
/// <para>
/// WeatherAPI receives its API key in the query string, which could leak through HTTP logs.
/// </para>
/// <para>
/// Verifies that the dedicated logging handler masks query strings and URLs in exceptions regardless
/// of the <c>System.Net.Http.DisableUriRedaction</c> setting.
/// </para>
/// </remarks>
public sealed class SecretsInLogTests
{
    private const string Secret = "SECRET-WEATHER-KEY-9z8y7x-do-not-log";

    [Fact]
    public async Task 天気_API_キーを_HTTP_クライアントログへ出力しない()
    {
        var (lines, _) = await CallAsync();

        lines.ShouldNotBeEmpty("ログ出力がないため、秘密情報のマスクを検証できません。");

        var leaked = lines.Where(line => line.Contains(Secret, StringComparison.Ordinal)).ToArray();

        leaked.ShouldBeEmpty($"API キーが {leaked.Length} 件のログに含まれています。");
    }

    [Fact]
    public async Task クエリ文字列全体を_ログへ出力しない()
    {
        // Remove the entire query so newly added parameters cannot leak secrets.
        var (lines, _) = await CallAsync();

        lines.ShouldAllBe(line => !line.Contains("key=", StringComparison.Ordinal));
        lines.ShouldAllBe(line => !line.Contains('?', StringComparison.Ordinal));
    }

    [Fact]
    public async Task 診断に必要な_URI_情報は残す()
    {
        // Preserve scheme, host, and path for endpoint diagnostics.
        var (lines, _) = await CallAsync();

        var start = lines.FirstOrDefault(line => line.Contains("stage=start", StringComparison.Ordinal));

        start.ShouldNotBeNull("HTTP リクエストの開始ログが見つかりません。");
        start.ShouldContain("method=GET");
        start.ShouldContain("/current.json");
    }

    [Fact]
    public async Task 依存サービスが失敗しても_API_キーを出力しない()
    {
        // Do not leave the API key in error logs that include exceptions.
        var (lines, _) = await CallAsync(fail: true);

        lines.Where(line => line.Contains(Secret, StringComparison.Ordinal)).ShouldBeEmpty();
        lines.ShouldContain(line => line.Contains("stage=failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 例外メッセージに_URL_が含まれても_API_キーをマスクする()
    {
        // Remove secrets when a handler or HTTP library includes a URL in an exception.
        var (lines, _) = await CallAsync(fail: true);

        var failed = lines.Single(line => line.Contains("stage=failed", StringComparison.Ordinal));

        failed.ShouldNotContain(Secret);
        failed.ShouldContain("***", Case.Sensitive);

        // Preserve the error type for diagnostics after masking.
        failed.ShouldContain("HttpRequestException");
        failed.ShouldContain("cannot reach");
    }

    [Fact]
    public async Task Capability_の失敗ログにも_API_キーを出力しない()
    {
        var lines = new List<string>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StackChan:Weather:Endpoint"] = "http://weather.example/v1",
                ["StackChan:Weather:ApiKey"] = Secret,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new CaptureProvider(lines));
        });
        services.AddWeatherCapability(configuration);
        services.AddHttpClient(WeatherCapability.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new ThrowingHandler());

        await using var provider = services.BuildServiceProvider();
        var capability = provider.GetServices<ICapability>()
            .OfType<WeatherCapability>()
            .Single();

        await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        lines.ShouldContain(line =>
            line.Contains("capability name=get_current_weather stage=failed", StringComparison.Ordinal));
        lines.ShouldAllBe(line => !line.Contains(Secret, StringComparison.Ordinal));
    }

    /// <summary>
    /// Sends one request through the dedicated HTTP logger and collects emitted logs.
    /// </summary>
    private static async Task<(string[] Lines, HttpResponseMessage? Response)> CallAsync(
        bool fail = false)
    {
        var lines = new List<string>();

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new CaptureProvider(lines));
        });

        // Register the logging handler under test with the HTTP client.
        services.Configure<WeatherOptions>(weather => weather.ApiKey = Secret);
        services.AddSingleton<QueryFreeHttpLogger>();

        var builder = services
            .AddHttpClient("weather-probe", http => http.Timeout = TimeSpan.FromSeconds(3))
            .RemoveAllLoggers()
            .AddLogger<QueryFreeHttpLogger>();

        // Replace only the transport result and use the real logging handler.
        builder.ConfigurePrimaryHttpMessageHandler(() => fail
            ? new ThrowingHandler()
            : new RespondingHandler());

        await using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("weather-probe");

        var url = "http://weather.example/v1/current.json" +
            $"?key={Uri.EscapeDataString(Secret)}&q=Tokyo&aqi=no";

        HttpResponseMessage? response = null;

        try
        {
            response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
        }

        return ([.. lines], response);
    }

    /// <summary>The outermost HTTP handler that returns a successful response.</summary>
    private sealed class RespondingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"current":{"temp_c":24.0}}"""),
                RequestMessage = request,
            });
    }

    /// <summary>The outermost HTTP handler that throws an exception containing a URL.</summary>
    /// <remarks>
    /// Includes a URL in the exception message like a real HTTP library to verify masking.
    /// </remarks>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException($"cannot reach {request.RequestUri}");
    }

    private sealed class CaptureProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, sink);

        public void Dispose() { }
    }

    private sealed class CaptureLogger(string category, List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            // Collect the complete exception so secrets in exception text can also be detected.
            sink.Add($"[{logLevel}] {category}: {formatter(state, exception)} {exception}");
        }
    }
}
