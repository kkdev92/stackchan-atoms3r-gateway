using System.Globalization;
using System.Net;
using RichardSzalay.MockHttp;
using Shouldly;
using Xunit;

namespace StackChan.Capability.Weather.Tests;

/// <summary>
/// Verifies behavior of the weather capability.
/// </summary>
/// <remarks>
/// Covers WeatherAPI requests, spoken response text, and fallback behavior for external-service failures.
/// </remarks>
public sealed class WeatherCapabilityTests
{
    private static WeatherOptions Options => new()
    {
        Endpoint = "https://api.weatherapi.com/v1",
        ApiKey = "test-key",
        DefaultLocation = "Tokyo",
        Language = "ja",
        TimeoutSeconds = 10,
    };

    /// <summary>Represents only WeatherAPI response fields needed by tests.</summary>
    private static string Body(
        string name = "Tokyo",
        double temperature = 28.3,
        string condition = "晴れ",
        double? feelsLike = null)
    {
        var temp = temperature.ToString(CultureInfo.InvariantCulture);
        var feels = (feelsLike ?? temperature).ToString(CultureInfo.InvariantCulture);

        return $$"""
            {
              "location": { "name": "{{name}}", "country": "Japan" },
              "current": {
                "temp_c": {{temp}},
                "condition": { "text": "{{condition}}" },
                "humidity": 60,
                "wind_kph": 12.6,
                "feelslike_c": {{feels}}
              }
            }
            """;
    }

    [Fact]
    public async Task WeatherAPI_の仕様に従って要求を組み立てる()
    {
        using var handler = new MockHttpMessageHandler();
        Uri? requested = null;

        handler.When(HttpMethod.Get, "*")
            .With(request =>
            {
                requested = request.RequestUri;
                return true;
            })
            .Respond("application/json", Body());

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        requested.ShouldNotBeNull();
        requested.AbsolutePath.ShouldBe("/v1/current.json");
        requested.Scheme.ShouldBe("https");
        requested.Query.ShouldContain("key=test-key");
        requested.Query.ShouldContain("q=Tokyo");
        requested.Query.ShouldContain("lang=ja");

        // Do not request unused air-quality data, keeping the response small.
        requested.Query.ShouldContain("aqi=no");
    }

    [Fact]
    public async Task 場所を省略したら_設定済みの場所を使う()
    {
        using var handler = new MockHttpMessageHandler();
        Uri? requested = null;

        handler.When(HttpMethod.Get, "*")
            .With(request => { requested = request.RequestUri; return true; })
            .Respond("application/json", Body("Osaka"));

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(
            () => client,
            new WeatherOptions { ApiKey = "k", DefaultLocation = "Osaka" });

        await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        requested!.Query.ShouldContain("q=Osaka");
    }

    [Fact]
    public async Task 場所を指定したら_設定値より優先する()
    {
        using var handler = new MockHttpMessageHandler();
        Uri? requested = null;

        handler.When(HttpMethod.Get, "*")
            .With(request => { requested = request.RequestUri; return true; })
            .Respond("application/json", Body("Sapporo"));

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        await capability.GetCurrentWeatherAsync(
            "札幌", TestContext.Current.CancellationToken);

        // URL-encode location names containing Japanese text.
        requested!.Query.ShouldContain("q=" + Uri.EscapeDataString("札幌"));
    }

    [Fact]
    public async Task 天気情報を_読み上げ可能な_1_文に整形する()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond("application/json", Body());

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        var answer = await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        answer.ShouldBe("Tokyoの天気は晴れ、気温は28.3度です。");
    }

    [Fact]
    public async Task 体感温度との差が大きい場合だけ_体感温度を付け加える()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond("application/json", Body(temperature: 30.0, feelsLike: 35.2));

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        var answer = await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        answer.ShouldContain("体感は35.2度");
    }

    [Fact]
    public async Task 体感温度との差が小さい場合は_体感温度を省略する()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond("application/json", Body(temperature: 20.0, feelsLike: 20.5));

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        var answer = await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        answer.ShouldNotContain("体感");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HTTP_エラー時は_会話を失敗させず取得不能と返す(HttpStatusCode status)
    {
        // Allow the turn to continue when weather data cannot be retrieved.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond(status);

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        var answer = await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        answer.ShouldBe(WeatherCapability.Unavailable);
    }

    [Fact]
    public async Task 接続できない場合も_取得不能と返す()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Throw(new HttpRequestException("no route"));

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        (await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .ShouldBe(WeatherCapability.Unavailable);
    }

    [Fact]
    public async Task 不正な応答でも_取得不能と返す()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond("application/json", "{ not json");

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        (await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .ShouldBe(WeatherCapability.Unavailable);
    }

    [Fact]
    public async Task 必須フィールドが欠けていたら_天気情報を組み立てない()
    {
        // Do not speak a missing temperature as the default value of zero degrees.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond("application/json", """{"location":{"name":"Tokyo"},"current":{"humidity":60}}""");

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        (await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .ShouldBe(WeatherCapability.Unavailable);
    }

    [Fact]
    public async Task タイムアウトしても_会話を失敗させない()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(
            () => client,
            new WeatherOptions { ApiKey = "k", TimeoutSeconds = 1 });

        (await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .ShouldBe(WeatherCapability.Unavailable);
    }

    [Fact]
    public async Task 呼び出し元からのキャンセルは_そのまま伝播する()
    {
        // Distinguish device-disconnect cancellation from an internal timeout and propagate it to the caller.
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        using var cancellation = new CancellationTokenSource();
        var running = capability.GetCurrentWeatherAsync(null, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task API_キーを_戻り値に含めない()
    {
        using var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, "*").Respond("application/json", Body());

        using var client = handler.ToHttpClient();
        var capability = new WeatherCapability(() => client, Options);

        var answer = await capability.GetCurrentWeatherAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        answer.ShouldNotContain("test-key");
    }
}
