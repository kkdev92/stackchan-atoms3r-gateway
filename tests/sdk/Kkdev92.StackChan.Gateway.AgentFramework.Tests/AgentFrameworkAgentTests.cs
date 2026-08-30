using Kkdev92.StackChan.Gateway.Abstractions;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Tests;

/// <summary>
/// Verifies agent behavior built on Agent Framework.
/// </summary>
/// <remarks>
/// Covers streamed model responses, per-session history, and public API dependencies while replacing
/// the model itself with a test double.
/// </remarks>
public sealed class AgentFrameworkAgentTests
{
    /// <summary>
    /// System instructions passed to the model in tests.
    /// </summary>
    /// <remarks>
    /// The SDK has no default persona or language because consumers supply them. This value is used
    /// only to verify that supplied instructions reach the model unchanged.
    /// </remarks>
    private const string TestInstructions = "みじかく答えて。表情の目印を付けて。";

    private static readonly AgentFrameworkOptions Options = new()
    {
        Endpoint = "http://127.0.0.1:1234/v1",
        Model = "test-model",
        Name = "StackChan",
        MaxOutputTokens = 512,
        Instructions = TestInstructions,
    };

    private static AgentRequest Request(string session, string text) =>
        new(new SessionId(session), new DeviceId(session), text);

    [Fact]
    public async Task 応答本文の断片だけを_受信順に返す()
    {
        var model = new FakeChatClient();
        model.Rounds.Add([
            new TextContent("[happy]こんにちは。"),
            new TextContent("[neutral]今日は良い天気です。"),
        ]);

        var agent = new AgentFrameworkAgent(Options, [], _ => model);

        var spoken = await Collect(agent, Request("atoms3r-1", "やあ"));

        spoken.ShouldBe(["[happy]こんにちは。", "[neutral]今日は良い天気です。"]);
    }

    [Fact]
    public async Task 推論テキストは_応答ストリームへ含めない()
    {
        var model = new FakeChatClient();
        model.Rounds.Add([
            new TextReasoningContent("ユーザーは挨拶している。まず挨拶を返す。"),
            new TextContent("[happy]こんにちは。"),
        ]);

        var agent = new AgentFrameworkAgent(Options, [], _ => model);

        var spoken = await Collect(agent, Request("atoms3r-1", "やあ"));

        spoken.ShouldBe(["[happy]こんにちは。"]);
        spoken.ShouldAllBe(text => !text.Contains("ユーザーは挨拶", StringComparison.Ordinal));
    }

    [Fact]
    public async Task モデルの応答断片を_順番に返す()
    {
        var model = new FakeChatClient();
        model.Rounds.Add([
            new TextContent("[happy]さいしょ。"),
            new TextContent("[neutral]あと。"),
        ]);

        var agent = new AgentFrameworkAgent(Options, [], _ => model);

        var received = new List<string>();

        await foreach (var text in agent.StreamAsync(
            Request("atoms3r-1", "やあ"), TestContext.Current.CancellationToken))
        {
            received.Add(text);
        }

        received.ShouldBe(["[happy]さいしょ。", "[neutral]あと。"]);
    }

    [Fact]
    public async Task 同じセッション_ID_なら_履歴を引き継ぐ()
    {
        var model = new FakeChatClient();
        model.Rounds.Add([new TextContent("[neutral]ヤマダさんですね。")]);
        model.Rounds.Add([new TextContent("[neutral]ヤマダさんです。")]);

        var agent = new AgentFrameworkAgent(Options, [], _ => model);

        await Collect(agent, Request("atoms3r-1", "私の名前はヤマダです"));
        await Collect(agent, Request("atoms3r-1", "私の名前は？"));

        model.Calls.ShouldBe(2);

        // Include the previous user input and assistant response in the second request.
        var second = model.Seen[1];
        second.Count.ShouldBe(3);
        second[0].Text.ShouldBe("私の名前はヤマダです");
        second[1].Text.ShouldBe("[neutral]ヤマダさんですね。");
        second[2].Text.ShouldBe("私の名前は？");
    }

    [Fact]
    public async Task 履歴を_設定したメッセージ数に制限する()
    {
        // Limit retained history so long conversations stay within the model context window.
        var options = new AgentFrameworkOptions
        {
            Endpoint = Options.Endpoint,
            Model = Options.Model,
            Name = Options.Name,
            MaxOutputTokens = Options.MaxOutputTokens,
            MaxHistoryMessages = 4,
        };

        var model = new FakeChatClient();
        var agent = new AgentFrameworkAgent(options, [], _ => model);

        for (var turn = 1; turn <= 6; turn++)
        {
            await Collect(agent, Request("atoms3r-1", $"{turn} 回目です"));
        }

        model.Calls.ShouldBe(6);

        // Retain four history messages and add the current user input to the model request.
        model.Seen[1].Count.ShouldBe(3);
        model.Seen[2].Count.ShouldBe(5);
        model.Seen[3].Count.ShouldBe(5);
        model.Seen[^1].Count.ShouldBe(5);

        // Preserve recent history and current user input after reaching the limit.
        model.Seen[^1][^1].Text.ShouldBe("6 回目です");
        model.Seen[^1].ShouldAllBe(message => message.Text != "1 回目です");
    }

    [Fact]
    public async Task 異なるセッション_ID_の履歴を混在させない()
    {
        var model = new FakeChatClient();
        var agent = new AgentFrameworkAgent(Options, [], _ => model);

        await Collect(agent, Request("atoms3r-1", "1 台目です"));
        await Collect(agent, Request("atoms3r-2", "2 台目です"));

        model.Seen[1].Count.ShouldBe(1);
        model.Seen[1][0].Text.ShouldBe("2 台目です");
    }

    [Fact]
    public async Task システム指示とツールを_モデルへ渡す()
    {
        var model = new FakeChatClient();
        var agent = new AgentFrameworkAgent(
            Options,
            [new ProbeCapability()],
            _ => model);

        await Collect(agent, Request("atoms3r-1", "やあ"));

        var options = model.Options[0].ShouldNotBeNull();
        options.Instructions.ShouldBe(TestInstructions);
        options.ModelId.ShouldBe("test-model");
        options.MaxOutputTokens.ShouldBe(512);
        options.Tools.ShouldNotBeNull();
        options.Tools.OfType<AIFunction>().Select(tool => tool.Name)
            .ShouldContain("get_current_time");
    }

    [Fact]
    public async Task 設定したシステム指示を使用する()
    {
        var model = new FakeChatClient();
        var agent = new AgentFrameworkAgent(
            new AgentFrameworkOptions
            {
                Endpoint = "http://127.0.0.1:1234/v1",
                Model = "test-model",
                Instructions = "みじかく答えて。",
            },
            [],
            _ => model);

        await Collect(agent, Request("atoms3r-1", "やあ"));

        model.Options[0]!.Instructions.ShouldBe("みじかく答えて。");
    }

    [Fact]
    public async Task モデルへ接続できない場合は_unavailable_エラーへ変換する()
    {
        var agent = new AgentFrameworkAgent(
            Options,
            [],
            _ => new ThrowingChatClient(new HttpRequestException("no route")));

        var exception = await Should.ThrowAsync<ProviderException>(
            () => Collect(agent, Request("atoms3r-1", "やあ")));

        exception.Code.ShouldBe(GatewayErrorCode.Unavailable);
        exception.Retryable.ShouldBeTrue();
        exception.Message.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task 想定外の例外は_internal_エラーへ変換する()
    {
        var agent = new AgentFrameworkAgent(
            Options,
            [],
            _ => new ThrowingChatClient(new InvalidOperationException("boom")));

        var exception = await Should.ThrowAsync<ProviderException>(
            () => Collect(agent, Request("atoms3r-1", "やあ")));

        exception.Code.ShouldBe(GatewayErrorCode.Internal);
        exception.Message.ShouldBe("unexpected gateway error");
    }

    [Fact]
    public async Task 呼び出し元からのキャンセルは_そのまま伝播する()
    {
        var model = new FakeChatClient { Block = new TaskCompletionSource() };
        var agent = new AgentFrameworkAgent(Options, [], _ => model);

        using var cancellation = new CancellationTokenSource();
        var running = Collect(agent, Request("atoms3r-1", "やあ"), cancellation.Token);

        while (model.Calls == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => running);
        model.ObservedCancellation.ShouldBeTrue();
    }

    [Fact]
    public void 公開_API_に_Agent_Framework_固有の型を公開しない()
    {
        var type = typeof(AgentFrameworkAgent);

        foreach (var member in type.GetMembers())
        {
            var signature = member.ToString() ?? "";

            signature.ShouldNotContain("Microsoft.Agents");
            signature.ShouldNotContain("Microsoft.Extensions.AI");
            signature.ShouldNotContain("OpenAI");
        }
    }

    private static async Task<List<string>> Collect(
        AgentFrameworkAgent agent,
        AgentRequest request,
        CancellationToken? cancellationToken = null)
    {
        var spoken = new List<string>();
        var token = cancellationToken ?? TestContext.Current.CancellationToken;

        await foreach (var text in agent.StreamAsync(request, token))
        {
            spoken.Add(text);
        }

        return spoken;
    }

    /// <summary>A capability used to verify that tools are passed to the model.</summary>
    private sealed class ProbeCapability : ICapability
    {
        [CapabilityAction("get_current_time", "現在の日付と時刻を取得します。")]
        public string GetCurrentTime() => "2026年8月22日 土曜日 12時00分";
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162 // Unreachable code: required to make this method an iterator.
            yield break;
#pragma warning restore CS0162
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw exception;

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
