using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>
/// Verifies that an unset identifier never crosses a turn boundary.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SessionId"/> and <see cref="DeviceId"/> are readonly record structs, so a
/// <c>default</c> value can be created without going through the constructor. Accepting an
/// unset value would mix several conversations into one session with a null identifier, so
/// turn creation rejects it as well.
/// </para>
/// </remarks>
public sealed class IdentityGuardTests
{
    [Fact]
    public void default_の_SessionId_は未設定になる()
    {
        default(SessionId).IsSet.ShouldBeFalse();
        new SessionId("s1").IsSet.ShouldBeTrue();
    }

    [Fact]
    public void default_の_DeviceId_は未設定になる()
    {
        default(DeviceId).IsSet.ShouldBeFalse();
        new DeviceId("atoms3r-001122334455").IsSet.ShouldBeTrue();
    }

    [Fact]
    public void 未設定の_SessionId_ではターンを作成できない()
    {
        var device = new DeviceTurnContext(new DeviceId("d1"), "b", "c");

        Should.Throw<ArgumentException>(
            () => TurnRequest.FromText(default, device, "こんにちは"))
            .Message.ShouldContain("Session id");
    }

    [Fact]
    public void 未設定の_DeviceId_ではデバイスコンテキストを作成できない()
    {
        Should.Throw<ArgumentException>(
            () => new DeviceTurnContext(default, "b", "c"))
            .Message.ShouldContain("Device id");
    }

    [Fact]
    public void 設定済みの_ID_ならターンを作成できる()
    {
        var request = TurnRequest.FromText(
            new SessionId("s1"),
            new DeviceTurnContext(new DeviceId("d1"), "b", "c"),
            "こんにちは");

        request.SessionId.Value.ShouldBe("s1");
        request.Device.DeviceId.Value.ShouldBe("d1");
    }
}
