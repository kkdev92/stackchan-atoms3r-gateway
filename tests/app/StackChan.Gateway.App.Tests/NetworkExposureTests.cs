using Shouldly;
using StackChan.Gateway.App.Security;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies rejection at startup of unsafe unauthenticated LAN exposure.
/// </summary>
/// <remarks>
/// Reject only when both an authentication token is absent and a non-loopback listener is configured.
/// This permits local development and authenticated LAN exposure.
/// </remarks>
public sealed class NetworkExposureTests
{
    private const string Lan = "http://0.0.0.0:8787";
    private const string Loopback = "http://127.0.0.1:8787";

    [Fact]
    public void 認証トークンなしの_LAN_公開を拒否する()
    {
        var risk = NetworkExposure.DescribeRisk(
            Lan, hasToken: false, allowUnauthenticatedLan: false);

        risk.ShouldNotBeNull();

        // Include configuration steps for safe startup in the error.
        risk.ShouldContain("StackChan__Atoms3R__Token");
        risk.ShouldContain(NetworkExposure.AllowUnauthenticatedLanKey);
    }

    [Fact]
    public void 認証トークンがあれば_LAN_公開を許可する()
    {
        NetworkExposure.DescribeRisk(Lan, hasToken: true, allowUnauthenticatedLan: false)
            .ShouldBeNull();
    }

    [Fact]
    public void ループバックなら_認証トークンなしでも許可する()
    {
        NetworkExposure.DescribeRisk(Loopback, hasToken: false, allowUnauthenticatedLan: false)
            .ShouldBeNull();
    }

    [Fact]
    public void 明示的に許可されていれば_認証なしの公開を許可する()
    {
        // Permit unauthenticated exposure only through explicit opt-in.
        NetworkExposure.DescribeRisk(Lan, hasToken: false, allowUnauthenticatedLan: true)
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("http://127.0.0.1:8787")]
    [InlineData("http://localhost:8787")]
    [InlineData("http://[::1]:8787")]
    [InlineData("http://127.0.0.1:8787;http://localhost:8788")]
    [InlineData(null)]
    [InlineData("")]
    public void 外部公開しない待ち受け先を判定する(string? urls) =>
        NetworkExposure.IsLoopbackOnly(urls).ShouldBeTrue();

    [Theory]
    [InlineData("http://0.0.0.0:8787")]
    [InlineData("http://[::]:8787")]
    [InlineData("http://*:8787")]
    [InlineData("http://+:8787")]
    [InlineData("http://192.168.0.10:8787")]
    // Treat the configuration as externally exposed if any listener is not loopback.
    [InlineData("http://127.0.0.1:8787;http://0.0.0.0:8788")]
    // Fail safely by treating an unparseable value as externally exposed.
    [InlineData("not a url")]
    public void 外部公開する待ち受け先を判定する(string urls) =>
        NetworkExposure.IsLoopbackOnly(urls).ShouldBeFalse();
}
