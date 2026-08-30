using System.Net;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies how agent sessions are identified and evicted.
/// </summary>
/// <remarks>
/// <para>
/// The protocol has device, boot, and conversation identifiers, but only the device ID identifies
/// agent history. <c>BootId</c> and <c>ConversationId</c> are not passed to the model.
/// </para>
/// <list type="table">
///   <item><term><c>X-StackChan-Device</c></term><description>
///     Identifies the session (<c>SessionId = DeviceId</c>).
///   </description></item>
///   <item><term><c>X-StackChan-Boot</c></term><description>
///     Correlates requests. A restart does not discard history.
///   </description></item>
///   <item><term><c>X-StackChan-Conversation</c></term><description>
///     Correlates requests. Starting a new conversation does not discard history.
///   </description></item>
/// </list>
/// <para>
/// History is evicted when inactivity exceeds <c>StackChan:Agent:SessionIdleTimeoutMinutes</c> or
/// session count exceeds <c>StackChan:Agent:MaxSessions</c>. There is no API for explicitly resetting a session.
/// </para>
/// </remarks>
public sealed class SessionLifecycleTests
{
    private const string Device = "atoms3r-001122334455";

    [Fact]
    public async Task BootId_が変わっても_同じセッションを継続する()
    {
        // Preserve history for the same DeviceId even when a restart changes BootId.
        var agent = await AskTwiceAsync(
            firstBoot: "3T4YFZQ9K7M2NBPXVWCDEG5HJ0",
            secondBoot: "0HJ5GEDCWVXPBN2M7K9QZFY4T3");

        agent.Requests.Count.ShouldBe(2);
        agent.Requests[1].SessionId.ShouldBe(agent.Requests[0].SessionId);
    }

    [Fact]
    public async Task ConversationId_が変わっても_同じセッションを継続する()
    {
        var agent = await AskTwiceAsync(
            firstConversation: "conv-1",
            secondConversation: "conv-2");

        agent.Requests.Count.ShouldBe(2);
        agent.Requests[1].SessionId.ShouldBe(agent.Requests[0].SessionId);
    }

    [Fact]
    public async Task DeviceId_が異なれば_別のセッションになる()
    {
        var agent = await AskTwiceAsync(
            firstDevice: Device,
            secondDevice: "atoms3r-aabbccddeeff");

        agent.Requests.Count.ShouldBe(2);
        agent.Requests[1].SessionId.ShouldNotBe(agent.Requests[0].SessionId);
    }

    [Fact]
    public async Task SessionId_には_DeviceId_を使用する()
    {
        // Fix the session identity rule explicitly.
        var agent = await AskTwiceAsync();

        agent.Requests[0].SessionId.Value.ShouldBe(Device);
        agent.Requests[0].DeviceId.Value.ShouldBe(Device);
    }

    /// <summary>
    /// Sends two requests and returns the requests received by the agent.
    /// </summary>
    private static async Task<FakeAgent> AskTwiceAsync(
        string firstDevice = Device,
        string secondDevice = Device,
        string firstBoot = DeviceRequest.DefaultBoot,
        string secondBoot = DeviceRequest.DefaultBoot,
        string firstConversation = "conv-1",
        string secondConversation = "conv-1")
    {
        await using var factory = new GatewayFactory();
        factory.SpeechToText.Result = "こんにちは";
        factory.Agent.Fragments = ["はい、こんにちは。"];
        factory.TextToSpeech.Result = new PcmAudio(
            new short[400], PcmAudio.CanonicalSampleRate, PcmAudio.CanonicalChannels);

        using var client = factory.CreateClient();

        await AskAsync(client, firstDevice, firstBoot, firstConversation);
        await AskAsync(client, secondDevice, secondBoot, secondConversation);

        return factory.Agent;
    }

    private static async Task AskAsync(
        HttpClient client,
        string device,
        string boot,
        string conversation)
    {
        // DeviceRequest fixes BootId, so construct a request with an arbitrary value here.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/converse")
        {
            Content = new StringContent(
                """{"text":"こんにちは"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        request.Headers.Add("Accept", "text/event-stream");
        request.Headers.Add("X-StackChan-Device", device);
        request.Headers.Add("X-StackChan-Boot", boot);
        request.Headers.Add("X-StackChan-Conversation", conversation);

        using var response = await client.SendAsync(
            request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Read the response body through completion to finish the turn.
        await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }
}
