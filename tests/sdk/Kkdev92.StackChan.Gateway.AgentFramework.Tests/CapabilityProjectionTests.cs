using System.Text.Json;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.AgentFramework.Tools;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Tests;

/// <summary>
/// Verifies rules for projecting capabilities to Agent Framework tools.
/// </summary>
/// <remarks>
/// Keeps capability implementations independent of Agent Framework and detects invalid declarations at startup.
/// </remarks>
public sealed class CapabilityProjectionTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 20, 12, 34, 0, TimeSpan.FromHours(9));

    [Fact]
    public void Capability_の名前と説明を_ツールへ投影する()
    {
        var tools = CapabilityToolProjector.Project([new ClockCapability(TimeProvider.System)]).Tools;

        var tool = tools.ShouldHaveSingleItem().ShouldBeAssignableTo<AIFunction>();
        tool.Name.ShouldBe("get_current_time");
        tool.Description.ShouldBe("現在の日付と時刻を取得します。時刻を聞かれたら必ずこれを使ってください。");
    }

    [Fact]
    public async Task 投影したツールを呼ぶと_Capability_を実行する()
    {
        var clock = new FixedTimeProvider(Noon);
        var tools = CapabilityToolProjector.Project([new ClockCapability(clock)]).Tools;
        var tool = (AIFunction)tools[0];

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        Value(result).ShouldBe("2026年8月20日 木曜日 12時34分");
    }

    [Fact]
    public void Capability_は_ツールを介さずに直接呼び出せる()
    {
        // Capability implementations can be used independently of Agent Framework.
        var capability = new ClockCapability(new FixedTimeProvider(Noon));

        capability.GetCurrentTime().ShouldBe("2026年8月20日 木曜日 12時34分");
    }

    [Fact]
    public void ツール名が重複していたら_投影時に拒否する()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CapabilityToolProjector.Project(
                [new ClockCapability(TimeProvider.System), new DuplicateTimeCapability()]));

        exception.Message.ShouldContain("get_current_time");
    }

    [Fact]
    public void 静的メソッドに_CapabilityFunction_属性が付いていたら拒否する()
    {
        Should.Throw<InvalidOperationException>(
            () => CapabilityToolProjector.Project([new StaticActionCapability()]).Tools)
            .Message.ShouldContain("static");
    }

    [Fact]
    public void ジェネリックメソッドに_CapabilityFunction_属性が付いていたら拒否する()
    {
        Should.Throw<InvalidOperationException>(
            () => CapabilityToolProjector.Project([new GenericActionCapability()]).Tools)
            .Message.ShouldContain("generic");
    }

    [Fact]
    public void ref_引数を持つ_Capability_メソッドを拒否する()
    {
        Should.Throw<InvalidOperationException>(
            () => CapabilityToolProjector.Project([new RefActionCapability()]).Tools)
            .Message.ShouldContain("ref");
    }

    [Fact]
    public void CancellationToken_が末尾でなければ拒否する()
    {
        Should.Throw<InvalidOperationException>(
            () => CapabilityToolProjector.Project([new MisplacedTokenCapability()]).Tools)
            .Message.ShouldContain("cancellation token");
    }

    [Fact]
    public void CapabilityFunction_属性のないメソッドは投影しない()
    {
        var tools = CapabilityToolProjector.Project([new PartiallyMarkedCapability()]).Tools;

        tools.ShouldHaveSingleItem();
        ((AIFunction)tools[0]).Name.ShouldBe("marked_action");
    }

    [Fact]
    public void トリガー語句を_ツール名とともに投影する()
    {
        // Capabilities declare triggers for models that cannot select tools reliably.
        var projection = CapabilityToolProjector.Project([new ClockCapability(TimeProvider.System)]);

        projection.Triggers.Keys.ShouldHaveSingleItem().ShouldBe("get_current_time");
        projection.Triggers["get_current_time"].ShouldContain("何時");
        projection.Triggers["get_current_time"].ShouldContain("日付");
    }

    [Fact]
    public void トリガーを宣言しない_Capability_は_トリガー一覧に含めない()
    {
        var projection = CapabilityToolProjector.Project([new PartiallyMarkedCapability()]);

        projection.Tools.ShouldHaveSingleItem();
        projection.Triggers.ShouldBeEmpty();
    }

    [Fact]
    public void 省略可能な引数は_JSON_Schema_で必須にしない()
    {
        // CapabilityPrefetchChatClient does not prefetch tools with required arguments, so an argument
        // with a default value must not appear in required.
        var tools = CapabilityToolProjector.Project([new OptionalArgumentCapability()]).Tools;
        var tool = (AIFunction)tools[0];

        var schema = tool.JsonSchema;
        schema.TryGetProperty("properties", out var properties).ShouldBeTrue();
        properties.TryGetProperty("where", out _).ShouldBeTrue();

        if (schema.TryGetProperty("required", out var required) &&
            required.ValueKind == JsonValueKind.Array)
        {
            required.GetArrayLength().ShouldBe(
                0, "既定値のある引数が必須になると、トリガーによる事前実行を利用できません。");
        }
    }

    [Fact]
    public void CancellationToken_はツール引数へ投影しない()
    {
        var tools = CapabilityToolProjector.Project([new OptionalArgumentCapability()]).Tools;

        if (((AIFunction)tools[0]).JsonSchema.TryGetProperty("properties", out var properties))
        {
            properties.TryGetProperty("cancellationToken", out _).ShouldBeFalse();
        }
    }

    [Fact]
    public void 省略可能な引数の_type_を配列にしない()
    {
        // Foundry Local expects JSON Schema type to be a string and rejects the entire request when a
        // nullable type is emitted as the ["string", "null"] array.
        var tools = CapabilityToolProjector.Project([new OptionalArgumentCapability()]).Tools;
        var schema = ((AIFunction)tools[0]).JsonSchema;

        schema.TryGetProperty("properties", out var properties).ShouldBeTrue();
        properties.TryGetProperty("where", out var where).ShouldBeTrue();
        where.TryGetProperty("type", out var type).ShouldBeTrue();

        type.ValueKind.ShouldBe(
            JsonValueKind.String,
            "type を配列にすると、ツール定義を含むリクエストがモデルに拒否される");
        type.GetString().ShouldBe("string");
    }

    [Fact]
    public void 投影したすべてのツールで_type_を文字列として出力する()
    {
        // Scan the complete projected schema so newly added capabilities are covered.
        var tools = CapabilityToolProjector.Project(
            [new ClockCapability(TimeProvider.System), new OptionalArgumentCapability()]).Tools;

        foreach (var tool in tools.Cast<AIFunction>())
        {
            ArrayTypedNodes(tool.JsonSchema).ShouldBeEmpty(
                $"{tool.Name} のスキーマに配列の型が残っている");
        }
    }

    /// <summary>Scans a schema and returns paths whose <c>type</c> is an array.</summary>
    private static List<string> ArrayTypedNodes(JsonElement element, string path = "$")
    {
        var found = new List<string>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("type") &&
                        property.Value.ValueKind == JsonValueKind.Array)
                    {
                        found.Add(path);
                    }

                    found.AddRange(ArrayTypedNodes(property.Value, $"{path}.{property.Name}"));
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    found.AddRange(ArrayTypedNodes(item, $"{path}[{index++}]"));
                }

                break;

            default:
                break;
        }

        return found;
    }

    [Fact]
    public void 空の説明を持つ属性を拒否する()
    {
        Should.Throw<ArgumentException>(() => new CapabilityActionAttribute("name", "  "));
    }

    private static string Value(object? result) => result switch
    {
        JsonElement json => json.ValueKind == JsonValueKind.String
            ? json.GetString() ?? ""
            : json.GetRawText(),
        null => "",
        _ => result.ToString() ?? "",
    };

    [Fact]
    public void 外部状態を変更する_Capability_のトリガーは投影しない()
    {
        // Trigger-based prefetch bypasses model judgment and is therefore limited to read-only capabilities.
        var projection = CapabilityToolProjector.Project([new SwitchCapability()]);

        // Still expose it as a tool so it can run when selected by the model.
        projection.Tools.ShouldHaveSingleItem();

        // Exclude only the prefetch trigger.
        projection.Triggers.ShouldBeEmpty();
    }

    [Fact]
    public void 読み取り専用を宣言していない_Capability_のトリガーは投影しない()
    {
        // Treat undeclared capabilities as potentially state-changing so they are not prefetched accidentally.
        var projection = CapabilityToolProjector.Project([new UnmarkedCapability()]);

        projection.Tools.ShouldHaveSingleItem();
        projection.Triggers.ShouldBeEmpty();
    }

    /// <summary>A state-changing capability that must not be prefetched by a trigger.</summary>
    private sealed class SwitchCapability : ICapability
    {
        [CapabilityAction(
            "turn_off_light",
            "電気を消します。",
            IsReadOnly = false,
            Triggers = ["電気", "消して"])]
        public string TurnOff() => "消しました。";
    }

    /// <summary>A capability that does not declare whether it is read-only.</summary>
    private sealed class UnmarkedCapability : ICapability
    {
        [CapabilityAction("do_something", "何かします。", Triggers = ["何か"])]
        public string DoSomething() => "しました。";
    }

    private sealed class ClockCapability(TimeProvider timeProvider) : ICapability
    {
        [CapabilityAction(
            "get_current_time",
            "現在の日付と時刻を取得します。時刻を聞かれたら必ずこれを使ってください。",
            IsReadOnly = true,
            Triggers = ["何時", "なんじ", "時刻", "何日", "なんにち", "日付", "曜日"])]
        public string GetCurrentTime() =>
            timeProvider.GetLocalNow().ToString(
                "yyyy年M月d日 dddd HH時mm分", new System.Globalization.CultureInfo("ja-JP"));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("test", now.Offset, "test", "test");
    }

    /// <summary>A capability with an optional argument and a cancellation token.</summary>
    private sealed class OptionalArgumentCapability : ICapability
    {
        [CapabilityAction("look_up", "どこかを調べます。")]
        public Task<string> LookUpAsync(
            string? where = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(where ?? "既定");
    }

    private sealed class DuplicateTimeCapability : ICapability
    {
        [CapabilityAction("get_current_time", "重なった名前。")]
        public string Now() => "";
    }

    private sealed class StaticActionCapability : ICapability
    {
        [CapabilityAction("static_action", "静的メソッド。")]
        public static string Now() => "";
    }

    private sealed class GenericActionCapability : ICapability
    {
        [CapabilityAction("generic_action", "総称メソッド。")]
        public string Now<T>() => "";
    }

    private sealed class RefActionCapability : ICapability
    {
        [CapabilityAction("ref_action", "ref 引数。")]
        public string Now(ref int value) => value.ToString();
    }

    private sealed class MisplacedTokenCapability : ICapability
    {
        [CapabilityAction("misplaced_token", "中断のトークンが先頭。")]
        public string Now(CancellationToken cancellationToken, int value) => value.ToString();
    }

    private sealed class PartiallyMarkedCapability : ICapability
    {
        [CapabilityAction("marked_action", "印の付いたメソッド。")]
        public string Marked() => "";

        public string Unmarked() => "";
    }
}
