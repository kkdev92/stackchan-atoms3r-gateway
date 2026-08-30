namespace StackChan.Provider.WhisperCpp;

/// <summary>Configures speech recognition with whisper.cpp.</summary>
public sealed class WhisperCppOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "StackChan:WhisperCpp";

    /// <summary>The whisper.cpp server base URL. The default port is 8081.</summary>
    public string Endpoint { get; set; } = "http://127.0.0.1:8081";

    /// <summary>The speech recognition API path.</summary>
    public string Path { get; set; } = "/inference";

    /// <summary>The language to recognize. Use <c>auto</c> for automatic detection.</summary>
    public string Language { get; set; } = "ja";

    /// <summary>The number of seconds to wait for one recognition request.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>The maximum JSON response size accepted from the speech recognition API, in bytes.</summary>
    /// <remarks>
    /// The limit is applied before loading the response into memory to prevent excessive allocation.
    /// </remarks>
    public int MaxResponseBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>The minimum language probability required to accept a recognition result.</summary>
    /// <remarks>
    /// <para>
    /// Results below this threshold are treated as empty utterances to reduce cases where whisper.cpp
    /// recognizes non-speech as speech. Automatic language detection can assign relatively high
    /// probabilities to non-speech, so increase the threshold if needed. A value of 0 disables this
    /// check and returns the API response format to <c>json</c>.
    /// </para>
    /// </remarks>
    public double MinLanguageProbability { get; set; } = 0.5;
}
