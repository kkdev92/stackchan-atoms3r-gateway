using Kkdev92.StackChan.Gateway.Abstractions;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.AgentFramework.Tests;

/// <summary>A test TimeProvider that can advance to an arbitrary time.</summary>
/// <remarks>
/// Used to verify session idle expiration without waiting in real time.
/// </remarks>
internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>
/// Verifies session-history limits and idle expiration.
/// </summary>
/// <remarks>
/// Without a session count limit, history remains for every distinct session ID. An evicted session
/// restarts as a new session with empty history.
/// </remarks>
public sealed class AgentSessionHardeningTests
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static AgentRequest Request(string session, string text) =>
        new(new SessionId(session), new DeviceId(session), text);

    private static AgentFrameworkOptions Options(int maxSessions, int idleMinutes) => new()
    {
        Endpoint = "http://127.0.0.1:1234/v1",
        Model = "test-model",
        Name = "StackChan",
        MaxOutputTokens = 512,
        Instructions = "みじかく答えて。",
        MaxSessions = maxSessions,
        SessionIdleTimeoutMinutes = idleMinutes,
    };

    [Fact]
    public async Task 上限を超えたら_最終アクセスが最も古いセッションを破棄する()
    {
        var clock = new TestTimeProvider(Origin);
        var model = new FakeChatClient();

        // Use a long idle timeout so only the session count limit is exercised.
        var agent = new AgentFrameworkAgent(
            Options(maxSessions: 1, idleMinutes: 600), [], _ => model, clock);

        await Collect(agent, Request("atoms3r-a", "1 台目です"));
        clock.Advance(TimeSpan.FromMinutes(1));
        await Collect(agent, Request("atoms3r-a", "覚えていますか"));

        // Include previous input and response in history for the same session.
        model.Seen[1].Count.ShouldBe(3);

        clock.Advance(TimeSpan.FromMinutes(1));
        await Collect(agent, Request("atoms3r-b", "2 台目です"));

        // Evict session a, the least recently accessed, when the limit is exceeded.
        clock.Advance(TimeSpan.FromMinutes(1));
        await Collect(agent, Request("atoms3r-b", "覚えていますか"));

        model.Seen[3].Count.ShouldBe(3);

        // Restart evicted session a without history.
        clock.Advance(TimeSpan.FromMinutes(1));
        await Collect(agent, Request("atoms3r-a", "また来ました"));

        model.Seen[4].Count.ShouldBe(1);
        model.Seen[4][0].Text.ShouldBe("また来ました");
    }

    [Fact]
    public async Task アイドル期限を超えたセッションの履歴を破棄する()
    {
        var clock = new TestTimeProvider(Origin);
        var model = new FakeChatClient();

        var agent = new AgentFrameworkAgent(
            Options(maxSessions: 1, idleMinutes: 1), [], _ => model, clock);

        await Collect(agent, Request("atoms3r-a", "1 台目です"));
        await Collect(agent, Request("atoms3r-a", "覚えていますか"));

        model.Seen[1].Count.ShouldBe(3);

        // Expire session a and add another session.
        clock.Advance(TimeSpan.FromMinutes(2));
        await Collect(agent, Request("atoms3r-b", "2 台目です"));

        // Session b remains within its timeout and retains history for the next request.
        clock.Advance(TimeSpan.FromSeconds(10));
        await Collect(agent, Request("atoms3r-b", "覚えていますか"));

        model.Seen[3].Count.ShouldBe(3);

        await Collect(agent, Request("atoms3r-a", "また来ました"));

        model.Seen[4].Count.ShouldBe(1);
        model.Seen[4][0].Text.ShouldBe("また来ました");
    }

    private static async Task<List<string>> Collect(AgentFrameworkAgent agent, AgentRequest request)
    {
        var spoken = new List<string>();

        await foreach (var text in agent.StreamAsync(
            request, TestContext.Current.CancellationToken))
        {
            spoken.Add(text);
        }

        return spoken;
    }
}
