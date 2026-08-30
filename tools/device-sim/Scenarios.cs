using System.Globalization;
using System.Text;
using Kkdev92.StackChan.Gateway.TestKit;

namespace StackChan.DeviceSim;

/// <summary>One validation result within a scenario.</summary>
internal sealed record Verdict(string Name, bool Passed, string Detail);

/// <summary>Validates malformed input and communication boundary conditions.</summary>
/// <remarks>
/// These reproducible scenarios cover concurrency, disconnection, long input, and malformed formats,
/// allowing protocol handling and resource cleanup to be checked without a physical device.
/// </remarks>
internal static class Scenarios
{
    /// <summary>Runs consecutive conversations and checks that processing time does not keep increasing.</summary>
    public static async Task<IReadOnlyList<Verdict>> TurnsAsync(Settings settings)
    {
        var verdicts = new List<Verdict>();
        var elapsed = new List<double>();

        for (var turn = 1; turn <= settings.Turns; turn++)
        {
            var result = await Converse.RunAsync(
                Requests.Normal(settings, $"turn-{turn}", text: Spoken(settings)),
                settings.SlowReadMs);

            elapsed.Add(result.Total.TotalMilliseconds);

            verdicts.Add(new Verdict(
                $"turn {turn}",
                result.Status == 200 && result.Violations.Count == 0,
                $"{result.Total.TotalMilliseconds:F0} ms / {result.Finished ?? "(no completion event)"}" +
                Failed(result)));
        }

        // With bounded session history, processing time should not rise continuously in later turns.
        if (elapsed.Count >= 4)
        {
            var firstHalf = elapsed.Take(elapsed.Count / 2).Average();
            var lastHalf = elapsed.Skip(elapsed.Count / 2).Average();

            verdicts.Add(new Verdict(
                "processing time remains bounded",
                lastHalf < firstHalf * 3,
                $"first half {firstHalf:F0} ms -> second half {lastHalf:F0} ms"));
        }

        return verdicts;
    }

    /// <summary>Starts conversations concurrently and checks concurrency limits and response format.</summary>
    public static async Task<IReadOnlyList<Verdict>> BusyAsync(Settings settings)
    {
        // Use distinct IDs to test the gateway-wide limit instead of per-device serialization.
        var running = Enumerable.Range(1, settings.Concurrent)
            .Select(index => Converse.RunAsync(
                Requests.Normal(
                settings, $"burst-{index}",
                device: $"{settings.Device}-{index}", text: Spoken(settings))))
            .ToArray();

        var results = await Task.WhenAll(running);

        var completed = results.Count(result => result.Finished == "completed");
        var busy = results.Count(result => result.ErrorCode == "busy");

        return
        [
            new Verdict(
                "concurrency is limited",
                completed > 0 && completed + busy == results.Length,
                $"{completed} completed and {busy} busy out of {results.Length}"),
            new Verdict(
                "busy responses conform to the protocol",
                results.All(result => result.Violations.Count == 0),
                results.Sum(result => result.Violations.Count) + " protocol violation(s)"),
        ];
    }

    /// <summary>Disconnects during a response and checks that the concurrency slot is released.</summary>
    public static async Task<IReadOnlyList<Verdict>> DisconnectAsync(Settings settings)
    {
        // When no delay is specified, disconnect after the first audio event to avoid response-time dependence.
        var cut = await Converse.RunAsync(
            Requests.Normal(settings, "cut", text: Spoken(settings)),
            disconnectAfterMs: settings.DisconnectAfterGiven ? settings.DisconnectAfterMs : 0,
            disconnectOnFirstAudio: !settings.DisconnectAfterGiven);

        // Start another conversation immediately to confirm that the execution slot was released.
        var after = await Converse.RunAsync(
            Requests.Normal(settings, "after-cut", text: Spoken(settings)));

        return
        [
            new Verdict(
                "client disconnects during a response",
                cut.Disconnected,
                cut.Disconnected
                    ? $"disconnected after receiving {cut.Body.Length} bytes"
                    : $"conversation ended before disconnection ({cut.Total.TotalMilliseconds:F0} ms)"),
            new Verdict(
                "another conversation starts after disconnection",
                after.Finished == "completed",
                $"{after.Total.TotalMilliseconds:F0} ms / {after.Finished ?? "(no completion event)"}" +
                Failed(after)),
        ];
    }

    /// <summary>Sends a long utterance and checks that response text stays within the size limit.</summary>
    public static async Task<IReadOnlyList<Verdict>> LongAsync(Settings settings)
    {
        // The Japanese character used here is three UTF-8 bytes, so the default 200 characters exceed 512 bytes.
        var spoken = new string('あ', settings.LongChars);

        var result = await Converse.RunAsync(Requests.Text(settings, spoken, "long"));

        var texts = SseWire.Events(result.Body)
            .Where(wire => wire.Name == "conversation.text")
            .Select(wire => wire.Payload.TryGetProperty("text", out var text)
                ? text.GetString() ?? ""
                : "")
            .ToArray();

        var longest = texts.Length == 0 ? 0 : texts.Max(text => Encoding.UTF8.GetByteCount(text));

        return
        [
            new Verdict(
                "text stays within 512 bytes",
                longest <= 512,
                $"input {Encoding.UTF8.GetByteCount(spoken)} bytes, largest response event {longest} bytes"),
            new Verdict(
                "long input still conforms to the protocol",
                result.Violations.Count == 0,
                result.Violations.Count == 0 ? "no violations" : string.Join(" / ", result.Violations)),
        ];
    }

    /// <summary>Checks status codes and error bodies for malformed requests.</summary>
    public static async Task<IReadOnlyList<Verdict>> RejectAsync(Settings settings)
    {
        var cases = Requests.Rejected(settings);
        var verdicts = new List<Verdict>();

        foreach (var (name, expectedStatus, expectedReason, request) in cases)
        {
            var result = await Converse.RunAsync(request);

            var matched = result.Status == expectedStatus &&
                (expectedReason.Length == 0 ||
                    (result.Reason?.Contains(expectedReason, StringComparison.Ordinal) ?? false));

            verdicts.Add(new Verdict(
                name,
                matched,
                $"{result.Status} {Short(result.Reason)} (expected: {expectedStatus} {expectedReason})"));
        }

        return verdicts;
    }

    private static string Spoken(Settings settings) =>
        settings.Text ?? Requests.DefaultUtterance;

    private static string Failed(ConverseResult result) =>
        result.Violations.Count == 0
            ? ""
            : " / violations: " + string.Join(" / ", result.Violations);

    private static string Short(string? reason) =>
        reason is null or "" ? "" :
        reason.Length <= 60 ? reason.ReplaceLineEndings(" ") :
        reason.ReplaceLineEndings(" ")[..60] + "…";

    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
