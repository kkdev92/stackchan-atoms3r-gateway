using System.Text;
using Kkdev92.StackChan.Gateway.TestKit;
using StackChan.DeviceSim;

// Call /v1/converse in place of a physical device and validate SSE format and response timing.
//
//   dotnet run --project tools/device-sim -- --gateway http://127.0.0.1:8787
//   dotnet run --project tools/device-sim -- --text hello --save reply.wav
//   dotnet run --project tools/device-sim -- --scenario all

// Use UTF-8 so Japanese input and validation results are displayed correctly.
Console.OutputEncoding = Encoding.UTF8;

var settings = Settings.Parse(args);

if (settings is null)
{
    Console.WriteLine("""
        Usage:
          --gateway <url>    Gateway base URL (default: http://127.0.0.1:8787)
          --token <token>    X-StackChan-Token (discouraged; use the environment variable below)
          --device <id>      X-StackChan-Device (default: atoms3r-simulator)
          --text <utterance> Start a conversation with text instead of audio
          --seconds <n>      Length of the WAV input in seconds (default: 2)
          --save <path>      Save received audio as a WAV file
          --slow-read <ms>   Delay after each read in milliseconds (default: 0)

        Validation scenarios:
          --scenario <name>  single | turns | busy | disconnect | long | reject | all
                             default: single (one conversation)
          --turns <n>              Number of conversations for turns (default: 6)
          --concurrent <n>         Concurrent requests for busy (default: 4)
          --disconnect-after <ms>  Delay before disconnecting in disconnect
                                   When omitted, disconnect just after the first audio event
          --long-chars <n>         Input length for long (default: 200 characters)

        Pass the authentication token through the STACKCHAN_DEVICE_TOKEN environment variable.
        The --token value may be visible in process listings and is not recommended.
        """);

    return 2;
}

Console.WriteLine($"gateway : {settings.Gateway}");
Console.WriteLine($"device  : {settings.Device}");
Console.WriteLine($"scenario: {settings.Scenario.ToString().ToLowerInvariant()}");

if (settings.Scenario is Scenario.Single)
{
    Console.WriteLine($"mode    : {(settings.Text is null ? $"audio {settings.Seconds}s" : "text")}");
}

if (settings.SlowReadMs > 0)
{
    Console.WriteLine($"slow    : {settings.SlowReadMs} ms/read");
}

Console.WriteLine();

return settings.Scenario is Scenario.Single
    ? await SingleAsync(settings)
    : await ScenarioAsync(settings);

static async Task<int> SingleAsync(Settings settings)
{
    var result = await Converse.RunAsync(
        Requests.Normal(settings, "sim-" + Environment.TickCount64),
        settings.SlowReadMs);

    Console.WriteLine($"status  : {result.Status}");
    Console.WriteLine($"type    : {result.ContentType}");
    Console.WriteLine($"headers : {result.ToHeaders.TotalMilliseconds:F0} ms");

    if (result.Reason is { Length: > 0 } reason)
    {
        Console.WriteLine(reason);

        return 1;
    }

    Console.WriteLine($"transcript : {Show(result.ToTranscript)}");
    Console.WriteLine($"first audio: {Show(result.ToFirstAudio)}");
    Console.WriteLine($"max gap    : {result.MaxGap.TotalMilliseconds:F0} ms");
    Console.WriteLine($"total      : {result.Total.TotalMilliseconds:F0} ms");
    Console.WriteLine($"body       : {result.Body.Length} bytes");
    Console.WriteLine();

    foreach (var wire in SseWire.Events(result.Body))
    {
        var payload = wire.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            ? Summarize(wire)
            : "";

        Console.WriteLine($"  {wire.Name,-24} {payload}");
    }

    Console.WriteLine();

    if (result.Violations.Count == 0)
    {
        Console.WriteLine("conformance: all 13 checks passed");
    }
    else
    {
        Console.WriteLine($"conformance: {result.Violations.Count} violation(s)");

        foreach (var violation in result.Violations)
        {
            Console.WriteLine($"  {violation}");
        }
    }

    // Also check protocol timing limits for response headers and gaps between events.
    if (result.ToHeaders > TimeSpan.FromSeconds(10))
    {
        Console.WriteLine(
            $"timing: response headers took {result.ToHeaders.TotalSeconds:F1} seconds (limit: 10 seconds)");
    }

    if (result.MaxGap > TimeSpan.FromSeconds(30))
    {
        Console.WriteLine(
            $"timing: gap between events was {result.MaxGap.TotalSeconds:F1} seconds (limit: 30 seconds)");
    }

    if (settings.Save is not null)
    {
        var samples = PcmFromEvents(result.Body);
        File.WriteAllBytes(settings.Save, WavFactory.Wav(samples, 16000, 1));
        Console.WriteLine($"saved  : {settings.Save} ({samples.Length / 2} samples)");
    }

    return result.Violations.Count == 0 ? 0 : 1;
}

static async Task<int> ScenarioAsync(Settings settings)
{
    var chosen = settings.Scenario is Scenario.All
        ? new[] { Scenario.Reject, Scenario.Long, Scenario.Disconnect, Scenario.Busy, Scenario.Turns }
        : [settings.Scenario];

    var failed = 0;

    foreach (var scenario in chosen)
    {
        var name = scenario.ToString().ToLowerInvariant();

        Console.WriteLine($"--- {name} ---");

        var verdicts = scenario switch
        {
            Scenario.Turns => await Scenarios.TurnsAsync(settings),
            Scenario.Busy => await Scenarios.BusyAsync(settings),
            Scenario.Disconnect => await Scenarios.DisconnectAsync(settings),
            Scenario.Long => await Scenarios.LongAsync(settings),
            Scenario.Reject => await Scenarios.RejectAsync(settings),
            _ => [],
        };

        foreach (var verdict in verdicts)
        {
            Console.WriteLine(
                $"  {(verdict.Passed ? "ok  " : "NG  ")}{verdict.Name,-26} {verdict.Detail}");

            if (!verdict.Passed)
            {
                failed++;
            }
        }

        Console.WriteLine();
    }

    Console.WriteLine(failed == 0
        ? "All validations passed."
        : $"{Scenarios.Count(failed)} validation(s) failed.");

    return failed == 0 ? 0 : 1;
}

static string Show(TimeSpan? at) =>
    at is null ? "(none)" : $"{at.Value.TotalMilliseconds:F0} ms";

static string Summarize(WireEvent wire)
{
    var payload = wire.Payload;
    var parts = new List<string>();

    if (payload.TryGetProperty("seq", out var seq))
    {
        parts.Add($"seq={seq.GetInt64()}");
    }

    if (payload.TryGetProperty("rate", out var rate))
    {
        parts.Add($"rate={rate.GetInt32()}");
    }

    if (payload.TryGetProperty("pcm", out var pcm))
    {
        // Show decoded size so it can be compared with the PCM chunk limit.
        var encoded = pcm.GetString() ?? "";
        var decoded = encoded.Length == 0 ? 0 : Convert.FromBase64String(encoded).Length;
        parts.Add($"pcm={decoded}B");
    }

    if (payload.TryGetProperty("last", out var last))
    {
        parts.Add($"last={last.GetBoolean()}");
    }

    if (payload.TryGetProperty("text", out var text))
    {
        parts.Add($"text=\"{text.GetString()}\"");
    }

    if (payload.TryGetProperty("reason", out var reason))
    {
        parts.Add($"reason={reason.GetString()}");
    }

    if (payload.TryGetProperty("code", out var code))
    {
        parts.Add($"code={code.GetString()}");
    }

    return string.Join(" ", parts);
}

static byte[] PcmFromEvents(byte[] body)
{
    using var pcm = new MemoryStream();

    foreach (var wire in SseWire.Events(body))
    {
        if (wire.Name != "reply.audio" ||
            !wire.Payload.TryGetProperty("pcm", out var encoded) ||
            encoded.GetString() is not { Length: > 0 } value)
        {
            continue;
        }

        var chunk = Convert.FromBase64String(value);
        pcm.Write(chunk, 0, chunk.Length);
    }

    return pcm.ToArray();
}
