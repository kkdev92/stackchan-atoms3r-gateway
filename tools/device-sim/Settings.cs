using System.Globalization;

namespace StackChan.DeviceSim;

/// <summary>A scenario run by Device Simulator.</summary>
internal enum Scenario
{
    Single,

    Turns,

    Busy,

    Disconnect,

    Long,

    Reject,

    All,
}

/// <summary>Execution settings parsed from the command line.</summary>
internal sealed record Settings(
    string Gateway,
    string Device,
    string? Token,
    string? Text,
    int Seconds,
    string? Save,
    int SlowReadMs,
    Scenario Scenario,
    int Turns,
    int Concurrent,
    int DisconnectAfterMs,
    bool DisconnectAfterGiven,
    int LongChars)
{
    public const string TokenVariable = "STACKCHAN_DEVICE_TOKEN";

    public static Settings? Parse(string[] args)
    {
        var gateway = "http://127.0.0.1:8787";
        var device = "atoms3r-simulator";
        string? token = null;
        string? text = null;
        string? save = null;
        var seconds = 2;
        var slowReadMs = 0;
        var scenario = Scenario.Single;
        var turns = 6;
        var concurrent = 4;
        var disconnectAfterMs = 1500;
        var disconnectAfterGiven = false;
        var longChars = 200;

        for (var index = 0; index < args.Length; index++)
        {
            var next = index + 1 < args.Length ? args[index + 1] : null;

            switch (args[index])
            {
                case "--gateway" when next is not null:
                    gateway = next.TrimEnd('/');
                    index++;
                    break;

                case "--device" when next is not null:
                    device = next;
                    index++;
                    break;

                case "--token" when next is not null:
                    token = next;
                    index++;
                    break;

                case "--text" when next is not null:
                    text = next;
                    index++;
                    break;

                case "--save" when next is not null:
                    save = next;
                    index++;
                    break;

                case "--slow-read" when next is not null &&
                    int.TryParse(next, CultureInfo.InvariantCulture, out var wait):
                    slowReadMs = Math.Clamp(wait, 0, 5000);
                    index++;
                    break;

                case "--seconds" when next is not null &&
                    int.TryParse(next, CultureInfo.InvariantCulture, out var value):
                    seconds = Math.Clamp(value, 1, 30);
                    index++;
                    break;

                case "--scenario" when next is not null && TryScenario(next, out var chosen):
                    scenario = chosen;
                    index++;
                    break;

                case "--turns" when next is not null &&
                    int.TryParse(next, CultureInfo.InvariantCulture, out var count):
                    turns = Math.Clamp(count, 1, 100);
                    index++;
                    break;

                case "--concurrent" when next is not null &&
                    int.TryParse(next, CultureInfo.InvariantCulture, out var parallel):
                    concurrent = Math.Clamp(parallel, 2, 32);
                    index++;
                    break;

                case "--disconnect-after" when next is not null &&
                    int.TryParse(next, CultureInfo.InvariantCulture, out var after):
                    disconnectAfterMs = Math.Clamp(after, 1, 60000);
                    disconnectAfterGiven = true;
                    index++;
                    break;

                case "--long-chars" when next is not null &&
                    int.TryParse(next, CultureInfo.InvariantCulture, out var chars):
                    longChars = Math.Clamp(chars, 1, 5000);
                    index++;
                    break;

                default:
                    return null;
            }
        }

        // Prefer the environment token because command-line arguments may be visible in process listings.
        if (Environment.GetEnvironmentVariable(TokenVariable) is { Length: > 0 } fromEnvironment)
        {
            token = fromEnvironment;
        }

        return new Settings(
            gateway, device, token, text, seconds, save, slowReadMs,
            scenario, turns, concurrent, disconnectAfterMs, disconnectAfterGiven,
            longChars);
    }

    private static bool TryScenario(string name, out Scenario scenario)
    {
        scenario = name switch
        {
            "single" => Scenario.Single,
            "turns" => Scenario.Turns,
            "busy" => Scenario.Busy,
            "disconnect" => Scenario.Disconnect,
            "long" => Scenario.Long,
            "reject" => Scenario.Reject,
            "all" => Scenario.All,
            _ => Scenario.Single,
        };

        return name is "single" or "turns" or "busy" or "disconnect" or "long" or "reject" or "all";
    }
}
