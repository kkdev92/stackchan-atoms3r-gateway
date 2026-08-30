using System.Net;
using System.Net.Sockets;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies that the three health checks each return their distinct state.
/// </summary>
/// <remarks>
/// Process liveness, conversation readiness, and dependency health have different consumers and
/// response times, so they are separate endpoints.
/// </remarks>
public sealed class HealthSemanticsTests
{
    [Fact]
    public async Task liveness_は_依存サービスへ接続せずに応答する()
    {
        // Liveness represents only whether the process can handle requests, not dependency state.
        await using var factory = new GatewayFactory
        {
            Offline = false,
            Token = "0123456789abcdef0123456789abcdef",
            Endpoints = new Dictionary<string, string>
            {
                ["stt"] = "http://127.0.0.1:1",
                ["tts"] = "http://127.0.0.1:1",
                ["model"] = "http://127.0.0.1:1/v1",
            },
        };

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("ok");
        body.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task readiness_は_アプリケーションの稼働中に_200_を返す()
    {
        await using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("ok");

        // Expose concurrency so callers can adjust request rate.
        body.ShouldContain("max_concurrent_turns");

        // Responses must not include credentials or endpoints.
        body.ShouldNotContain("127.0.0.1");
        body.ShouldNotContain("token");
    }

    /// <summary>A test endpoint that records the number of received requests.</summary>
    private static async Task AcceptAsync(
        TcpListener listener,
        Action onAccepted,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var socket = await listener.AcceptTcpClientAsync(cancellationToken);

                onAccepted();
            }
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
    }

    [Fact]
    public async Task 依存サービス確認は_結果をキャッシュして_外向き要求を抑制する()
    {
        // Ensure unauthenticated health checks do not load dependencies in proportion to call count.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var accepted = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var accepting = AcceptAsync(listener, () => Interlocked.Increment(ref accepted), stop.Token);

        await using var factory = new GatewayFactory
        {
            Offline = false,
            Token = "0123456789abcdef0123456789abcdef",
            Endpoints = new Dictionary<string, string>
            {
                ["stt"] = $"http://127.0.0.1:{port}",
                ["tts"] = $"http://127.0.0.1:{port}",
                ["model"] = $"http://127.0.0.1:{port}/v1",
            },
        };

        using var client = factory.CreateClient();

        for (var index = 0; index < 5; index++)
        {
            using var response = await client.GetAsync(
                "/health/providers", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await stop.CancelAsync();
        listener.Stop();
        await accepting;

        // Serve five checks with one request to each of the three dependencies.
        Volatile.Read(ref accepted).ShouldBe(
            3,
            $"依存サービスへ {accepted} 件の接続要求が送信された");
    }
}
