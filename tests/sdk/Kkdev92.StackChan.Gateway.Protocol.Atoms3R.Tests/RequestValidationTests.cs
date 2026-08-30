using System.Net;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Tests;

/// <summary>
/// Verifies that the conversation endpoint validates request headers and text format and size.
/// </summary>
/// <remarks>
/// Because host header limits vary by configuration, the endpoint applies protocol limits itself.
/// A malformed device ID must not start a turn.
/// </remarks>
public sealed class RequestValidationTests
{
    /// <summary>Representative identifiers that satisfy the device ID character and length constraints.</summary>
    /// <remarks>
    /// Verifies alphanumeric and hyphenated IDs used by Atoms3R as valid device-header examples.
    /// </remarks>
    [Theory]
    [InlineData("atoms3r-aabbccddeeff", "デバイス ID: atoms3r- と MAC アドレスの 16 進表記")]
    [InlineData("6HHB5RFM5CA6CETY4ZNVW4T2XY", "起動 ID: Crockford Base32 の 26 文字")]
    [InlineData(
        "6HHB5RFM5CA6CETY4ZNVW4T2XY-18446744073709551615",
        "会話 ID: 起動 ID と ulong 最大値の連番")]
    public async Task 許可された形式の識別子を_デバイスIDとして受け付ける(
        string device,
        string why)
    {
        (await StatusAsync(device: device)).ShouldBe(HttpStatusCode.OK, why);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(20)]
    public async Task デバイスIDは_128文字まで受け付ける(int length)
    {
        (await StatusAsync(device: new string('d', length))).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(129)]
    // Apply the protocol limit even below common host header limits.
    [InlineData(30000)]
    // Reject extremely large values even when host limits are relaxed.
    [InlineData(200000)]
    public async Task デバイスIDが_128文字を超えたら_400_を返す(int length)
    {
        var (status, body) = await ConverseAsync(device: new string('d', length));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("device header is malformed");
    }

    [Theory]
    // Whitespace makes log field boundaries ambiguous.
    [InlineData("atoms3r 001122334455")]
    // Control characters corrupt log structure.
    [InlineData("atoms3r-00\u0001")]
    // Non-ASCII characters can create visually similar but distinct identifiers.
    [InlineData("atoms3r-００１１")]
    // Quotes can be confused with structured-log delimiters.
    [InlineData("atoms3r-\"001\"")]
    public async Task デバイスIDに許可外の文字があれば_400_を返す(string device)
    {
        var (status, body) = await ConverseAsync(device: device);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("device header is malformed");
    }

    [Fact]
    public async Task 会話IDにも長さの上限を適用する()
    {
        var (status, body) = await ConverseAsync(conversation: new string('c', 200));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("conversation header is malformed");
    }

    [Fact]
    public async Task 会話IDは省略できる()
    {
        // The conversation ID is optional; omitting it can still start a new turn.
        (await StatusAsync(conversation: "")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task 不正なヘッダーでは_ターンを開始しない()
    {
        await using var host = await ProtocolHost.StartAsync();

        using var response = await host.Client.SendAsync(
            DeviceRequest.Text("こんにちは", device: new string('d', 200000)),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Validate before the runtime so malformed values are never retained as session keys.
        host.Runtime.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task 発話テキストが_4096_バイトを超えたら_413_を返す()
    {
        // Apply endpoint limits to direct callers other than the firmware.
        var (status, body) = await ConverseAsync(text: new string('あ', 2000));

        status.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        body.ShouldContain("text is too large");
    }

    [Fact]
    public async Task 発話テキストは_4096_バイトまで受け付ける()
    {
        // The Japanese character used here is three UTF-8 bytes, so 1,365 characters total 4,095 bytes.
        (await StatusAsync(text: new string('あ', 1365))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ファームウェア上限の発話テキストを受け付ける()
    {
        // The Atoms3R firmware limit is 480 bytes, equal to 160 of the Japanese characters used here.
        (await StatusAsync(text: new string('あ', 160))).ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpStatusCode> StatusAsync(
        string device = DeviceRequest.DefaultDevice,
        string conversation = "conv-1",
        string text = "こんにちは")
    {
        var (status, _) = await ConverseAsync(device, conversation, text);

        return status;
    }

    private static async Task<(HttpStatusCode Status, string Body)> ConverseAsync(
        string device = DeviceRequest.DefaultDevice,
        string conversation = "conv-1",
        string text = "こんにちは")
    {
        await using var host = await ProtocolHost.StartAsync();
        host.Runtime.Events.Add(
            new Abstractions.Turns.TurnCompleted(Abstractions.Turns.TurnCompletionReason.Completed));

        using var response = await host.Client.SendAsync(
            DeviceRequest.Text(text, device: device, conversation: conversation),
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response.StatusCode, body);
    }
}
