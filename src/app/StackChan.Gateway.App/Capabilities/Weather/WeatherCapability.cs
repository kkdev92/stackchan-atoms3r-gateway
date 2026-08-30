using System.Text.Json;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Capabilities;

namespace StackChan.Capability.Weather;

/// <summary>Retrieves current weather from WeatherAPI.com.</summary>
/// <remarks>
/// This capability is independent of agent-specific types and can be used on its own. It uses
/// <see cref="WeatherOptions.DefaultLocation"/> when no location is supplied. If weather data cannot
/// be retrieved, it returns an unavailable message instead of failing the entire conversation.
/// </remarks>
/// <param name="httpClient">A function that obtains the <see cref="HttpClient"/> used for each request.</param>
/// <param name="options">Settings including the endpoint, API key, and default location.</param>
/// <param name="logger">The logger for capability failures. HTTP exceptions containing URLs are replaced before logging.</param>
public sealed class WeatherCapability(
    Func<HttpClient> httpClient,
    WeatherOptions options,
    ILogger<WeatherCapability>? logger = null) : ICapability
{
    /// <summary>The name of the named <see cref="HttpClient"/> for the weather API.</summary>
    public const string HttpClientName = "weatherapi";

    /// <summary>The spoken message used when weather data cannot be retrieved.</summary>
    internal const string Unavailable = "天気の情報を取得できませんでした。";

    /// <summary>Returns current weather for the specified location.</summary>
    /// <param name="location">
    /// A location accepted by WeatherAPI.com, such as a city name. The configured default is used when omitted.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    [CapabilityAction(
        "get_current_weather",
        "指定した場所の現在の天気を取得します。天気を聞かれたら必ずこれを使ってください。場所を省略すると設定された場所の天気を返します。",
        IsReadOnly = true,
        Triggers = ["天気", "てんき", "気温", "きおん", "暑い", "寒い", "weather"])]
    public async Task<string> GetCurrentWeatherAsync(
        string? location = null,
        CancellationToken cancellationToken = default)
    {
        var place = string.IsNullOrWhiteSpace(location) ? options.DefaultLocation : location;

        return await CapabilityCall.AnswerAsync(
            async token =>
            {
                HttpResponseMessage response;

                try
                {
                    response = await httpClient()
                        .GetAsync(BuildUrl(place), token)
                        .ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // HttpRequestException may retain a RequestUri containing the query. Do not chain
                    // it, so CapabilityCall cannot log the API key with the exception.
                    throw new ProviderException(
                        GatewayErrorCode.Unavailable,
                        "weather API request failed",
                        retryable: true);
                }

                using (response)
                {
                    try
                    {
                        // Keep authentication and invalid-location errors identical for users; diagnose by status code.
                        response.EnsureSuccessStatusCode();
                    }
                    catch (HttpRequestException)
                    {
                        throw new ProviderException(
                            GatewayErrorCode.Unavailable,
                            $"weather API returned HTTP {(int)response.StatusCode}",
                            retryable: (int)response.StatusCode >= 500);
                    }

                    var json = await response.Content
                        .ReadAsStringAsync(token)
                        .ConfigureAwait(false);

                    return Describe(json) ?? Unavailable;
                }
            },
            Unavailable,
            TimeSpan.FromSeconds(options.TimeoutSeconds),
            cancellationToken,
            logger,
            "get_current_weather");
    }

    // URL-encode the API key and location because both are query parameters.
    private string BuildUrl(string place)
    {
        var url = options.Endpoint.TrimEnd('/') +
            "/current.json?key=" + Uri.EscapeDataString(options.ApiKey) +
            "&q=" + Uri.EscapeDataString(place) +
            "&aqi=no";

        if (!string.IsNullOrWhiteSpace(options.Language))
        {
            url += "&lang=" + Uri.EscapeDataString(options.Language);
        }

        return url;
    }

    internal static string? Describe(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("location", out var location) ||
            !root.TryGetProperty("current", out var current))
        {
            return null;
        }

        var name = location.TryGetProperty("name", out var nameValue) &&
            nameValue.ValueKind == JsonValueKind.String
            ? nameValue.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(name) ||
            !TryNumber(current, "temp_c", out var temperature))
        {
            return null;
        }

        var condition = current.TryGetProperty("condition", out var conditionValue) &&
            conditionValue.ValueKind == JsonValueKind.Object &&
            conditionValue.TryGetProperty("text", out var conditionText) &&
            conditionText.ValueKind == JsonValueKind.String
            ? conditionText.GetString()?.Trim()
            : null;

        var text = string.IsNullOrWhiteSpace(condition)
            ? $"{name}の気温は{SpokenText.Number(temperature)}度です。"
            : $"{name}の天気は{condition}、気温は{SpokenText.Number(temperature)}度です。";

        if (TryNumber(current, "feelslike_c", out var feelsLike) &&
            Math.Abs(feelsLike - temperature) >= 2)
        {
            // Mention perceived temperature only when the audible difference is meaningful.
            text += $"体感は{SpokenText.Number(feelsLike)}度くらいです。";
        }

        return text;
    }

    private static bool TryNumber(JsonElement parent, string name, out double value)
    {
        value = 0;

        return parent.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out value);
    }

}
