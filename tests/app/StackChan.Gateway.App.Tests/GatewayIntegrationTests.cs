using System.Net;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.AgentFramework;
using Kkdev92.StackChan.Gateway.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using StackChan.Capability.Time;
using StackChan.Provider.PiperPlus;
using StackChan.Provider.WhisperCpp;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Integration tests that use reference-app endpoints.
/// </summary>
/// <remarks>
/// Replaces only external services with test doubles and verifies the complete <c>Program.cs</c>
/// composition, including configuration, dependency injection, and endpoint mapping.
/// </remarks>
public sealed class GatewayIntegrationTests
{
    [Fact]
    public async Task health_は_ok_を返す()
    {
        await using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldBe("""{"status":"ok"}""");

        // Health responses must not include credentials or endpoints.
        body.ShouldNotContain("127.0.0.1");
        body.ShouldNotContain("token");
    }

    [Fact]
    public async Task 固定応答モードの依存サービス確認は_offline_を返す()
    {
        await using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/health/providers", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("offline");
        body.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task 依存サービスに接続できなければ_503_とサービス名および状態だけを返す()
    {
        // Use explicitly unused ports to isolate the test from services in the environment.
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
        using var response = await client.GetAsync(
            "/health/providers", TestContext.Current.CancellationToken);

        // All connections fail because no listeners are started.
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("down");
        body.ShouldContain("listening");
        body.ShouldContain("stt");
        body.ShouldContain("tts");
        body.ShouldContain("model");

        // The response must not include endpoint URIs.
        body.ShouldNotContain("127.0.0.1");
        body.ShouldNotContain("8081");
    }

    [Fact]
    public async Task 実プロバイダーの_DI_構成でも起動できる()
    {
        // Register typed HttpClient and Agent Framework, including configuration validation and capability projection.
        await using var factory = new GatewayFactory
        {
            Offline = false,
            Token = "0123456789abcdef0123456789abcdef",
        };

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.Services.GetRequiredService<ISpeechToText>()
            .ShouldBeOfType<WhisperCppSpeechToText>();
        factory.Services.GetRequiredService<ITextToSpeech>()
            .ShouldBeOfType<PiperPlusTextToSpeech>();
        factory.Services.GetRequiredService<IAgent>()
            .ShouldBeOfType<AgentFrameworkAgent>();
        factory.Services.GetServices<ICapability>()
            .ShouldHaveSingleItem().ShouldBeOfType<TimeCapability>();
    }

    [Fact]
    public async Task 会話を_音声認識から完了イベントまで処理する()
    {
        await using var factory = new GatewayFactory();
        factory.SpeechToText.Result = "こんにちは";
        factory.Agent.Fragments = ["[happy]こんにちは、スタックちゃんです。"];

        using var client = factory.CreateClient();
        using var request = DeviceRequest.Speech();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var events = SseWire.Events(body);

        events.Select(wire => wire.Name).ShouldBe([
            "conversation.started",
            "conversation.text",
            "reply.audio",
            "reply.audio",
            "conversation.finished",
        ]);

        // Values passed to each test double also match endpoint input.
        factory.SpeechToText.Calls.ShouldBe(1);
        factory.Agent.Requests.ShouldHaveSingleItem().UserText.ShouldBe("こんにちは");
        factory.TextToSpeech.Texts.ShouldHaveSingleItem()
            .ShouldBe("こんにちは、スタックちゃんです。");
    }

    [Fact]
    public async Task 同じデバイスからの_2_回目の要求も処理できる()
    {
        await using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        for (var turn = 0; turn < 2; turn++)
        {
            using var request = DeviceRequest.Speech();
            using var response = await client.SendAsync(
                request, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsByteArrayAsync(
                TestContext.Current.CancellationToken);
            SseWire.Events(body)[^1].Name.ShouldBe("conversation.finished");
        }

        factory.Agent.Requests.Count.ShouldBe(2);
        factory.Agent.Requests[0].SessionId.ShouldBe(factory.Agent.Requests[1].SessionId);
    }

    [Fact]
    public async Task 音声認識が失敗したら_unavailable_を返す()
    {
        await using var factory = new GatewayFactory();
        factory.SpeechToText.Throws = new ProviderException(
            GatewayErrorCode.Unavailable, "speech recognition failed", true);

        using var client = factory.CreateClient();
        using var request = DeviceRequest.Speech();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // A failure after SSE starts is reported as an error event within the HTTP 200 stream.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var events = SseWire.Events(body);

        var error = events.First(wire => wire.Name == "error.raised");
        error.Payload.GetProperty("code").GetString().ShouldBe("unavailable");
        error.Payload.GetProperty("retryable").GetBoolean().ShouldBeTrue();
        events[^1].Payload.GetProperty("reason").GetString().ShouldBe("failed");
    }

    [Fact]
    public async Task 認証トークンを設定すると_トークンなしの要求は_401_になる()
    {
        await using var factory = new GatewayFactory
        {
            Token = "0123456789abcdef0123456789abcdef",
        };

        using var client = factory.CreateClient();
        using var request = DeviceRequest.Speech();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        factory.SpeechToText.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task 同時実行枠が埋まっていれば_busy_イベントを返す()
    {
        await using var factory = new GatewayFactory { MaxConcurrentTurns = 1 };
        var block = new TaskCompletionSource();
        factory.SpeechToText.Block = block;

        using var client = factory.CreateClient();

        using var first = DeviceRequest.Speech();
        var running = client.SendAsync(first, TestContext.Current.CancellationToken);

        while (factory.SpeechToText.Calls == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        using var second = DeviceRequest.Speech(device: "atoms3r-bbbbbbbbbbbb");
        using var response = await client.SendAsync(second, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var events = SseWire.Events(body);

        events.First(wire => wire.Name == "error.raised")
            .Payload.GetProperty("code").GetString().ShouldBe("busy");
        events[^1].Payload.GetProperty("reason").GetString().ShouldBe("failed");

        factory.SpeechToText.Block = null;
        block.SetResult();
        using var completed = await running;
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = 30_000)]
    public async Task デバイスが切断したら_依存サービスの処理も中断する()
    {
        await using var factory = new GatewayFactory();
        var block = new TaskCompletionSource();
        factory.SpeechToText.Block = block;

        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();

        using var request = DeviceRequest.Speech();
        var running = client.SendAsync(request, cancellation.Token);

        while (factory.SpeechToText.Calls == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await cancellation.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(() => running);

        // Client-disconnect cancellation propagates to speech recognition.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!factory.SpeechToText.ObservedCancellation && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        factory.SpeechToText.ObservedCancellation.ShouldBeTrue();

        block.TrySetResult();
    }

    [Fact]
    public async Task テキスト入力も_音声入力と同じ会話処理を通る()
    {
        await using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        using var request = DeviceRequest.Text("おはよう");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.SpeechToText.Calls.ShouldBe(0);
        factory.Agent.Requests.ShouldHaveSingleItem().UserText.ShouldBe("おはよう");
    }
}
