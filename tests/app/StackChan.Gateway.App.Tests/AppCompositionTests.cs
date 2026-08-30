using Kkdev92.StackChan.Gateway.AgentFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies the boundaries the app must preserve as the composition root.
/// </summary>
/// <remarks>
/// Verifies that the app can be composed only from NuGet packages and that default agent instructions
/// are defined in the app rather than the SDK.
/// </remarks>
public sealed class AppCompositionTests
{
    [Fact]
    public void App_プロジェクトは_ProjectReference_を持たない()
    {
        var csproj = Path.Combine(
            RepositoryRoot(), "src", "app", "StackChan.Gateway.App", "StackChan.Gateway.App.csproj");

        File.Exists(csproj).ShouldBeTrue($"{csproj} が見つかりません。");

        // Allowing ProjectReference would remove the guarantee that published packages alone compose the app.
        var text = File.ReadAllText(csproj);

        text.Contains("<ProjectReference", StringComparison.Ordinal)
            .ShouldBeFalse("アプリの csproj に ProjectReference が含まれています。SDK はパッケージとして参照する必要があります。");
    }

    [Fact]
    public async Task 指示が空なら_App_の既定値を_options_へ設定する()
    {
        // The SDK has no default persona or language; app PostConfigure supplies an empty setting.
        await using var factory = new GatewayFactory();

        var options = factory.Services
            .GetRequiredService<IOptions<AgentFrameworkOptions>>().Value;

        options.Instructions.ShouldBe(AppDefaults.Instructions);

        // The default Japanese prompt defined by the app is reflected in Options unchanged.
        options.Instructions.ShouldStartWith("あなたは小型会話ロボット「スタックちゃん」です。");
    }

    /// <summary>Returns the repository root directory.</summary>
    /// <remarks>
    /// The search uses <c>Directory.Packages.props</c>, which exists only at the root. Solution files
    /// are not suitable because they are split between <c>src/sdk</c> and <c>src/app</c>.
    /// </remarks>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Directory.Packages.props が見つからない。");
    }
}
