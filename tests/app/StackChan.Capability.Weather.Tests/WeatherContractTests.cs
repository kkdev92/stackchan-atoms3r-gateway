using System.Reflection;
using Kkdev92.StackChan.Gateway.Abstractions;
using Shouldly;
using Xunit;

namespace StackChan.Capability.Weather.Tests;

/// <summary>
/// Verifies that the weather capability is declared according to the SDK contract.
/// </summary>
/// <remarks>
/// SDK tests cover projection to JSON Schema. These tests check the contract the application
/// defines: the name, the description, the triggers, and the optional location argument.
/// </remarks>
public sealed class WeatherContractTests
{
    private static MethodInfo Action =>
        typeof(WeatherCapability).GetMethod(nameof(WeatherCapability.GetCurrentWeatherAsync))
            .ShouldNotBeNull();

    [Fact]
    public void Capability_インターフェースを実装する()
    {
        typeof(ICapability).IsAssignableFrom(typeof(WeatherCapability)).ShouldBeTrue();
    }

    [Fact]
    public void ツール名と説明を宣言する()
    {
        var attribute = Action.GetCustomAttribute<CapabilityActionAttribute>().ShouldNotBeNull();

        attribute.Name.ShouldBe("get_current_weather");
        attribute.Description.ShouldContain("天気");
    }

    [Fact]
    public void 呼び出しトリガーを宣言する()
    {
        // Triggers let a model that cannot choose tools on its own still invoke the capability.
        var attribute = Action.GetCustomAttribute<CapabilityActionAttribute>().ShouldNotBeNull();

        attribute.Triggers.ShouldContain("天気");
        attribute.Triggers.ShouldContain("気温");
    }

    [Fact]
    public void 場所は_省略可能な引数として宣言する()
    {
        // A trigger call that omits the location uses the capability's default location.
        var location = Action.GetParameters()[0];

        location.Name.ShouldBe("location");
        location.IsOptional.ShouldBeTrue("場所を省略した問い合わせでは、設定の既定値を使う必要があります。");
    }

    [Fact]
    public void 最後の引数として_CancellationToken_を受け取る()
    {
        // Capability projection uses the trailing CancellationToken to propagate cancellation.
        var parameters = Action.GetParameters();

        parameters[^1].ParameterType.ShouldBe(typeof(CancellationToken));
    }
}
