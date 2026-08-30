namespace StackChan.Capability.Weather;

/// <summary>
/// Configures the weather capability.
/// </summary>
/// <remarks>
/// Do not store the API key in a configuration file. Pass it through the
/// <c>StackChan__Weather__ApiKey</c> environment variable. The application does not include the key
/// in logs or device responses.
/// </remarks>
public sealed class WeatherOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "StackChan:Weather";

    /// <summary>The WeatherAPI.com base URL.</summary>
    /// <remarks>
    /// HTTPS is the default to prevent sending the API key over an unencrypted connection.
    /// </remarks>
    public string Endpoint { get; set; } = "https://api.weatherapi.com/v1";

    /// <summary>The WeatherAPI.com API key. The capability cannot be registered when this is empty.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// The location used when a request does not specify one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can be a city name, latitude and longitude, postal code, or another value accepted by
    /// WeatherAPI.com's <c>q</c> parameter.
    /// </para>
    /// <para>
    /// <c>auto:ip</c> is also supported, but a location inferred from the source IP may not match the
    /// device's actual location.
    /// </para>
    /// </remarks>
    public string DefaultLocation { get; set; } = "Tokyo";

    /// <summary>The language code used for weather descriptions.</summary>
    /// <remarks>The default is Japanese. An empty value uses the API default, English.</remarks>
    public string Language { get; set; } = "ja";

    /// <summary>The number of seconds to wait for one API request.</summary>
    /// <remarks>
    /// The default is short because a conversation response cannot begin until this request completes or times out.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 10;
}
