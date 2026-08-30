using Kkdev92.StackChan.Gateway.AgentFramework.Models;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Tests;

/// <summary>
/// Verifies rules for invoking matching read-only capabilities before the model call.
/// </summary>
/// <remarks>
/// To let models without reliable tool selection answer with accurate values, the gateway invokes
/// capabilities whose triggers match and passes their results to the model as tool-call history.
/// </remarks>
public sealed class CapabilityPrefetchChatClientTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Triggers = new()
    {
        ["get_current_time"] = ["何時", "日付"],
    };

    private const string Answer = "2026年8月20日 木曜日 20時34分";

    [Fact]
    public async Task トリガーに一致したら_Capability_を事前実行して結果を渡す()
    {
        var (fake, ran) = await AskAsync("いま何時ですか");

        ran.ShouldBeTrue();

        var seen = fake.Seen[0];

        // Add the capability result to history as tool-call and tool-result messages.
        seen[^2].Contents.ShouldHaveSingleItem()
            .ShouldBeOfType<FunctionCallContent>().Name.ShouldBe("get_current_time");
        seen[^1].Contents.ShouldHaveSingleItem()
            .ShouldBeOfType<FunctionResultContent>().Result.ShouldBe(Answer);

        // Remove the prefetched tool so the model cannot invoke it again.
        fake.Options[0]!.ToolMode.ShouldBeOfType<NoneChatToolMode>();
    }

    [Fact]
    public async Task トリガーに一致しなければ_Capability_を実行しない()
    {
        var (fake, ran) = await AskAsync("こんにちは");

        ran.ShouldBeFalse();
        fake.Seen[0].Count.ShouldBe(1);
        fake.Options[0]!.ToolMode.ShouldBeNull();
    }

    [Fact]
    public async Task 現在のターンにツール結果があれば_Capability_を再実行しない()
    {
        // Reproduce the request that FunctionInvokingChatClient resends after invoking a tool.
        var (fake, ran) = await AskAsync("いま何時ですか", withToolResult: true);

        ran.ShouldBeFalse();
        fake.Options[0]!.ToolMode.ShouldBeNull();
    }

    [Fact]
    public async Task Capability_が失敗しても_ターンを継続する()
    {
        var (fake, _) = await AskAsync("いま何時ですか", failing: true);

        // Do not add a failed result to history; pass the original request to the model.
        fake.Seen[0].Count.ShouldBe(1);
        fake.Options[0]!.ToolMode.ShouldBeNull();
    }

    [Fact]
    public async Task 必須引数のある_Capability_は_事前実行しない()
    {
        using var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(fake, Triggers);

        var options = new ChatOptions
        {
            // Required argument values cannot be determined from user input alone.
            Tools = [AIFunctionFactory.Create(
                (string city) => $"{city} は晴れ", "get_current_time")],
        };

        await DrainAsync(client, options, "いま何時ですか");

        fake.Seen[0].Count.ShouldBe(1);
    }

    [Fact]
    public async Task ツールが無ければ_要求をそのまま渡す()
    {
        using var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(fake, Triggers);

        var options = new ChatOptions();

        await DrainAsync(client, options, "いま何時ですか");

        fake.Options[0].ShouldBeSameAs(options);
    }

    [Fact]
    public async Task 複数のトリガーに一致したら_すべての_Capability_を実行する()
    {
        // Invoking only one could make the model invent the missing value.
        var (fake, ran) = await AskManyAsync("今の天気と時刻を教えて");

        ran.ShouldBe(["weather", "clock"]);

        // Combine multiple capability results into one tool-result message.
        var seen = fake.Seen[0];
        Calls(seen).ShouldBe(["get_current_weather"]);

        // Separate results with sentence terminators and newlines so no delimiter is spoken.
        Results(seen).ShouldHaveSingleItem()
            .ShouldBe($"東京は晴れ。\n{Answer}。");
    }

    [Fact]
    public async Task Capability_は_トリガーが発話に現れる順で実行する()
    {
        // Use utterance order, independent of registration or dictionary enumeration order.
        var (_, ran) = await AskManyAsync("今の時刻と天気を教えて");

        ran.ShouldBe(["clock", "weather"]);
    }

    [Fact]
    public async Task 一部の_Capability_が失敗しても_成功した_Capability_の結果を渡す()
    {
        var (fake, ran) = await AskManyAsync("今の天気と時刻を教えて", failing: "weather");

        ran.ShouldBe(["weather", "clock"]);

        // Exclude failures and include only successful capabilities in the tool result.
        Calls(fake.Seen[0]).ShouldBe(["get_current_time"]);
        Results(fake.Seen[0]).ShouldHaveSingleItem().ShouldBe(Answer);
        fake.Options[0]!.ToolMode.ShouldBeOfType<NoneChatToolMode>();
    }

    [Fact]
    public async Task すべての_Capability_が失敗したら_元の要求をモデルへ渡す()
    {
        var (fake, _) = await AskManyAsync(
            "今の天気と時刻を教えて", failing: "weather", alsoFailing: "clock");

        Calls(fake.Seen[0]).ShouldBeEmpty();
        fake.Options[0]!.ToolMode.ShouldBeNull();
    }

    [Fact]
    public async Task Capability_が失敗したら_例外を処理する前にログへ記録する()
    {
        // Continue the turn while distinguishing a capability failure from model-invented success.
        var failures = new List<(string Name, string Message)>();

        using var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(
            fake,
            Triggers,
            onFailed: (name, exception) => failures.Add((name, exception.Message)));

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(
                string () => throw new InvalidOperationException("時刻を取得できない"),
                "get_current_time")],
        };

        await DrainAsync(client, options, "いま何時ですか");

        failures.ShouldHaveSingleItem();
        failures[0].Name.ShouldBe("get_current_time");
        failures[0].Message.ShouldBe("時刻を取得できない");

        // Continue the conversation by passing the original request to the model after logging.
        fake.Seen[0].Count.ShouldBe(1);
    }

    [Fact]
    public async Task Capability_が成功した場合は_エラーを記録しない()
    {
        var failures = 0;

        using var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(
            fake, Triggers, onFailed: (_, _) => failures++);

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => Answer, "get_current_time")],
        };

        await DrainAsync(client, options, "いま何時ですか");

        failures.ShouldBe(0);
    }

    [Fact]
    public async Task ログ出力が失敗しても_会話を継続する()
    {
        // Do not let an observability failure fail the turn.
        using var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(
            fake,
            Triggers,
            onFailed: (_, _) => throw new InvalidOperationException("ログ出力に失敗した"));

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(
                string () => throw new InvalidOperationException("時刻を取得できない"),
                "get_current_time")],
        };

        await DrainAsync(client, options, "いま何時ですか");

        fake.Seen[0].Count.ShouldBe(1);
    }

    /// <summary>Returns the tool result passed to the model.</summary>
    private static string[] Results(IEnumerable<ChatMessage> messages) =>
        [.. messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Select(result => result.Result?.ToString() ?? "")];

    /// <summary>Returns the tool-call name passed to the model.</summary>
    private static string[] Calls(IEnumerable<ChatMessage> messages) =>
        [.. messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(call => call.Name)];

    /// <summary>
    /// Registers and requests two capabilities, then returns their actual invocation order.
    /// </summary>
    private static async Task<(FakeChatClient Fake, string[] Ran)> AskManyAsync(
        string asked,
        string? failing = null,
        string? alsoFailing = null)
    {
        var triggers = new Dictionary<string, IReadOnlyList<string>>
        {
            ["get_current_weather"] = ["天気", "気温"],
            ["get_current_time"] = ["時刻", "何時"],
        };

        var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(fake, triggers);
        var ran = new List<string>();

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(
                    () =>
                    {
                        ran.Add("weather");

                        return failing == "weather" || alsoFailing == "weather"
                            ? throw new InvalidOperationException("天気サービスに接続できない")
                            : "東京は晴れ";
                    },
                    "get_current_weather"),
                AIFunctionFactory.Create(
                    () =>
                    {
                        ran.Add("clock");

                        return failing == "clock" || alsoFailing == "clock"
                            ? throw new InvalidOperationException("時刻を取得できない")
                            : Answer;
                    },
                    "get_current_time"),
            ],
        };

        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, asked)],
            options,
            TestContext.Current.CancellationToken))
        {
        }

        return (fake, [.. ran]);
    }

    [Fact]
    public async Task 前のターンにツール結果があっても_新しいターンでは_Capability_を実行する()
    {
        // If an old tool result in history is mistaken for a result from the current turn, the new
        // request will not prefetch and the model may invent a value.
        var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(fake, Triggers);
        var ran = false;

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(
                () => { ran = true; return Answer; }, "get_current_time")],
        };

        // Add new user input after prior input, tool call, result, and response messages.
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "きのうは何日でしたか"),
            new(ChatRole.Assistant, [new FunctionCallContent("old-1", "get_current_time")]),
            new(ChatRole.Tool, [new FunctionResultContent("old-1", "2026年8月19日")]),
            new(ChatRole.Assistant, "8月19日でした。"),
            new(ChatRole.User, "いま何時ですか"),
        ];

        await foreach (var _ in client.GetStreamingResponseAsync(
            messages, options, TestContext.Current.CancellationToken))
        {
        }

        ran.ShouldBeTrue("前のターンの実行結果によって、Capability の事前実行が省略されました。");
        fake.Options[0]!.ToolMode.ShouldBeOfType<NoneChatToolMode>();
    }

    [Fact]
    public async Task 現在のターンでツール実行済みなら_Capability_を再実行しない()
    {
        // In a FunctionInvokingChatClient retry, the tool result follows the latest user input.
        var fake = new FakeChatClient();
        using var client = new CapabilityPrefetchChatClient(fake, Triggers);
        var ran = false;

        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(
                () => { ran = true; return Answer; }, "get_current_time")],
        };

        List<ChatMessage> messages =
        [
            new(ChatRole.User, "いま何時ですか"),
            new(ChatRole.Assistant, [new FunctionCallContent("now-1", "get_current_time")]),
            new(ChatRole.Tool, [new FunctionResultContent("now-1", Answer)]),
        ];

        await foreach (var _ in client.GetStreamingResponseAsync(
            messages, options, TestContext.Current.CancellationToken))
        {
        }

        ran.ShouldBeFalse("現在のターンですでに実行した Capability が再実行されました。");
        fake.Options[0]!.ToolMode.ShouldBeNull();
    }

    private static async Task<(FakeChatClient Fake, bool Ran)> AskAsync(
        string asked,
        bool withToolResult = false,
        bool failing = false)
    {
        var fake = new FakeChatClient();
        var client = new CapabilityPrefetchChatClient(fake, Triggers);
        var ran = false;

        var options = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(
                    () =>
                    {
                        ran = true;

                        return failing
                            ? throw new InvalidOperationException("時刻を取得できない")
                            : Answer;
                    },
                    "get_current_time"),
            ],
        };

        var messages = new List<ChatMessage> { new(ChatRole.User, asked) };

        if (withToolResult)
        {
            messages.Add(new ChatMessage(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "get_current_time")]));
            messages.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent("call-1", Answer)]));
        }

        await foreach (var _ in client.GetStreamingResponseAsync(
            messages, options, TestContext.Current.CancellationToken))
        {
        }

        client.Dispose();

        // A capability counts as invoked even when it throws, so the caller determines success.
        return (fake, ran && !failing);
    }

    private static async Task DrainAsync(
        CapabilityPrefetchChatClient client,
        ChatOptions options,
        string asked)
    {
        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, asked)],
            options,
            TestContext.Current.CancellationToken))
        {
        }
    }
}
