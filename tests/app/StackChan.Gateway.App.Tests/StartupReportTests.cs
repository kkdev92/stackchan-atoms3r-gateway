using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies that startup logs record effective settings without secrets.
/// </summary>
/// <remarks>
/// <para>
/// Limits such as request size, response size, timeout, and output tokens are configurable. Recording
/// effective values at startup makes request rejection diagnosable.
/// </para>
/// <para>
/// Authentication tokens and other secret values must not be exposed when settings are logged.
/// </para>
/// </remarks>
public sealed class StartupReportTests : IAsyncLifetime
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private CapturingFactory _factory = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _factory = new CapturingFactory { Token = Token };

        // Startup logs are emitted when the host is created.
        using var client = _factory.CreateClient();

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public void 適用された上限を記録する()
    {
        var config = Config();

        // Record all major limits so rejected requests can be diagnosed.
        config.ShouldContain("MaxRequestBodyBytes=");
        config.ShouldContain("MaxSpokenTextBytes=");
        config.ShouldContain("TurnTimeoutSeconds=");
        config.ShouldContain("MaxConcurrentTurns=");
        config.ShouldContain("MaxSessions=");
    }

    [Fact]
    public void 設定グループごとに_1_行へまとめる()
    {
        var lines = _factory.Lines
            .Where(line => line.Contains("config section=", StringComparison.Ordinal))
            .ToArray();

        // Group related settings so startup logs are not filled with one line per value.
        lines.Length.ShouldBeInRange(2, 8);
        lines.ShouldContain(line => line.Contains("StackChan:Runtime", StringComparison.Ordinal));
        lines.ShouldContain(line => line.Contains("StackChan:Atoms3R", StringComparison.Ordinal));
    }

    [Fact]
    public void 認証トークンの値は記録しない()
    {
        var config = Config();

        config.ShouldNotContain(Token);

        // Record only presence and length, never the value itself.
        config.ShouldContain("Token=set(32)");
    }

    [Fact]
    public void 固定応答モードでは_依存サービスの設定を記録しない()
    {
        // Showing settings for unused dependencies could imply they are active.
        var config = Config();

        config.ShouldContain("Enabled=true");
        config.ShouldNotContain("StackChan:WhisperCpp");
        config.ShouldNotContain("StackChan:Agent");
    }

    private string Config() => string.Join(
        "\n",
        _factory.Lines.Where(line => line.Contains("config section=", StringComparison.Ordinal)));

    /// <summary>A test host that collects startup logs.</summary>
    private sealed class CapturingFactory : GatewayFactory
    {
        private readonly Lock _gate = new();

        private readonly List<string> _lines = [];

        public string[] Lines
        {
            get
            {
                lock (_gate)
                {
                    return [.. _lines];
                }
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureLogging(logging =>
                logging.AddProvider(new CaptureProvider(line =>
                {
                    lock (_gate)
                    {
                        _lines.Add(line);
                    }
                })));
        }
    }

    private sealed class CaptureProvider(Action<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(sink);

        public void Dispose() { }
    }

    private sealed class CaptureLogger(Action<string> sink) : ILogger
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

            sink(formatter(state, exception));
        }
    }
}
