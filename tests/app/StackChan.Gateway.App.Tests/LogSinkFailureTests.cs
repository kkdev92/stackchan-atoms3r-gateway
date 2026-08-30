using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.TestKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies that a log sink failure does not affect protocol responses.
/// </summary>
/// <remarks>
/// <para>
/// Logging must not determine conversation success. A turn response must remain intact even if the
/// logger throws because of disk exhaustion, network disconnection, formatting errors, or similar failures.
/// </para>
/// <para>
/// Injects a throwing log provider and compares response bytes with and without the failure.
/// </para>
/// <para>
/// Shares one <c>WebApplicationFactory</c> and toggles log failure per request.
/// </para>
/// </remarks>
public sealed class LogSinkFailureTests : IAsyncLifetime
{
    private const string Transcript = "こんにちは、元気ですか";

    private const string FirstSentence = "[happy]こんにちは、スタックちゃんです。";

    private const string SecondSentence = "[neutral]きょうは良い天気ですね。";

    private ThrowingLogFactory _factory = null!;

    private string? _quietType;

    private byte[] _quiet = [];

    private string? _thrownType;

    private byte[] _thrown = [];

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _factory = new ThrowingLogFactory();
        _factory.SpeechToText.Result = Transcript;
        _factory.Agent.Fragments = [FirstSentence, SecondSentence];
        _factory.TextToSpeech.Result = new PcmAudio(
            new short[2500], PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);

        // Obtain the normal response used as the comparison baseline.
        _factory.Throwing = false;
        (_quietType, _quiet) = await ConverseAsync();

        // Run the same conversation with every log write failing.
        _factory.Throwing = true;
        (_thrownType, _thrown) = await ConverseAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // This test covers logging during conversation processing, not host-shutdown logs.
        _factory.Throwing = false;

        await _factory.DisposeAsync();
    }

    [Fact]
    public void ログ出力がすべて失敗しても_プロトコル契約を満たす()
    {
        var violations = ConformanceChecks.Run(_thrownType, _thrown, [Transcript, "こんにちは"]);

        violations.ShouldBeEmpty(
            "違反: " + string.Join(" / ", violations.Select(violation => violation.ToString())));
    }

    [Fact]
    public void ログ出力がすべて失敗しても_応答バイト列は変わらない()
    {
        _thrownType.ShouldBe(_quietType);
        _thrown.Length.ShouldBe(_quiet.Length);
        Convert.ToBase64String(_thrown).ShouldBe(Convert.ToBase64String(_quiet));
    }

    [Fact]
    public void 通常時の応答も_プロトコル契約を満たす()
    {
        // Also ensure the baseline response itself conforms.
        ConformanceChecks.Run(_quietType, _quiet, [Transcript, "こんにちは"]).ShouldBeEmpty();
    }

    private async Task<(string? ContentType, byte[] Body)> ConverseAsync()
    {
        using var client = _factory.CreateClient();
        using var request = DeviceRequest.Speech(conversation: "conv-7");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return (
            response.Content.Headers.ContentType?.ToString(),
            await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A test host with a log provider that throws.</summary>
    private sealed class ThrowingLogFactory : GatewayFactory
    {
        /// <summary>Whether to throw during log writes.</summary>
        public bool Throwing { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(new ThrowingProvider(() => Throwing));
            });
        }
    }

    private sealed class ThrowingProvider(Func<bool> throwing) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ThrowingLogger(throwing);

        public void Dispose() { }
    }

    private sealed class ThrowingLogger(Func<bool> throwing) : ILogger
    {
        // Fail only actual log writes, not scope creation.
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
            if (throwing())
            {
                throw new IOException("ログ出力先の空き容量がない");
            }
        }
    }
}
