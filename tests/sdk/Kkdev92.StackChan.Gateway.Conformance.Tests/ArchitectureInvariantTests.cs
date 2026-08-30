using System.Reflection;
using System.Xml.Linq;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.AgentFramework;
using Kkdev92.StackChan.Gateway.Capabilities;
using Kkdev92.StackChan.Gateway.Diagnostics;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Endpoints;
using Kkdev92.StackChan.Gateway.Providers.Audio;
using Kkdev92.StackChan.Gateway.Runtime.Turns;
using Kkdev92.StackChan.Gateway.TestKit;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Conformance.Tests;

/// <summary>
/// Verifies dependency direction and public API boundaries among SDK packages.
/// </summary>
/// <remarks>
/// Inspects compiled assemblies and project definitions to keep consumer-visible dependencies within
/// the intended boundaries.
/// </remarks>
public sealed class ArchitectureInvariantTests
{
    /// <summary>Namespace prefixes that must not appear in public SDK APIs.</summary>
    /// <remarks>
    /// Keep AI-implementation types out of parameters and return values so consumers depend only on SDK contracts.
    /// </remarks>
    private static readonly string[] ForbiddenFrameworks =
    [
        "Microsoft.Agents",
        "Microsoft.Extensions.AI",
        "OpenAI",
    ];

    /// <summary>ASP.NET Core prefixes prohibited in public APIs outside the protocol layer.</summary>
    private const string AspNet = "Microsoft.AspNetCore";

    /// <summary>Assemblies inspected for public APIs and whether ASP.NET Core types are allowed.</summary>
    /// <remarks>
    /// Protocol.Atoms3R may use ASP.NET Core types because it exposes endpoint-mapping extensions.
    /// No other package may expose them.
    /// </remarks>
    public static TheoryData<string, Type, bool> PublicSurfaces() => new()
    {
        { "Abstractions", typeof(DeviceId), false },
        { "Runtime", typeof(TurnRuntime), false },
        { "Protocol.Atoms3R", typeof(ConverseEndpoint), true },
        { "Providers", typeof(WavAudio), false },
        { "Capabilities", typeof(CapabilityCall), false },
        { "AgentFramework", typeof(AgentFrameworkAgent), false },
        { "Diagnostics", typeof(FixedResponseAgent), false },
        { "TestKit", typeof(ConformanceChecks), false },
    };

    /// <summary>Package references allowed for each SDK project except Abstractions.</summary>
    /// <remarks>
    /// New direct dependencies must be added explicitly. This list limits SDK dependency scope and is
    /// separate from package version management.
    /// </remarks>
    private static readonly Dictionary<string, string[]> AllowedPackages = new(StringComparer.Ordinal)
    {
        ["Kkdev92.StackChan.Gateway.Runtime"] =
        [
            "Microsoft.Extensions.Options.ConfigurationExtensions",
            "Microsoft.Extensions.Logging.Abstractions",
        ],
        ["Kkdev92.StackChan.Gateway.Protocol.Atoms3R"] = [],
        ["Kkdev92.StackChan.Gateway.Providers"] = [],
        ["Kkdev92.StackChan.Gateway.Capabilities"] =
        [
            "Microsoft.Extensions.Logging.Abstractions",
        ],
        ["Kkdev92.StackChan.Gateway.AgentFramework"] =
        [
            "Microsoft.Agents.AI",
            "Microsoft.Agents.AI.OpenAI",
            "Microsoft.Extensions.AI",
            "OpenAI",
            "Microsoft.Extensions.Options.ConfigurationExtensions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
        ],
        ["Kkdev92.StackChan.Gateway.Diagnostics"] =
        [
            "Microsoft.Extensions.Options.ConfigurationExtensions",
        ],
        ["Kkdev92.StackChan.Gateway.TestKit"] = [],
    };

    /// <summary>SDK projects whose package references are inspected.</summary>
    public static TheoryData<string> PackagedProjects() =>
        [.. AllowedPackages.Keys];

    /// <summary>Prohibited references between SDK projects.</summary>
    public static TheoryData<string, string> ForbiddenReferences() => new()
    {
        { "Kkdev92.StackChan.Gateway.Runtime", "Kkdev92.StackChan.Gateway.Protocol.Atoms3R" },
        { "Kkdev92.StackChan.Gateway.Runtime", "Kkdev92.StackChan.Gateway.AgentFramework" },
        { "Kkdev92.StackChan.Gateway.Diagnostics", "Kkdev92.StackChan.Gateway.AgentFramework" },
        { "Kkdev92.StackChan.Gateway.Diagnostics", "Kkdev92.StackChan.Gateway.Protocol.Atoms3R" },
        { "Kkdev92.StackChan.Gateway.AgentFramework", "Kkdev92.StackChan.Gateway.Protocol.Atoms3R" },
        { "Kkdev92.StackChan.Gateway.Providers", "Kkdev92.StackChan.Gateway.Protocol.Atoms3R" },
        { "Kkdev92.StackChan.Gateway.Providers", "Kkdev92.StackChan.Gateway.AgentFramework" },
        { "Kkdev92.StackChan.Gateway.Providers", "Kkdev92.StackChan.Gateway.Runtime" },
        { "Kkdev92.StackChan.Gateway.Capabilities", "Kkdev92.StackChan.Gateway.AgentFramework" },
        { "Kkdev92.StackChan.Gateway.Capabilities", "Kkdev92.StackChan.Gateway.Protocol.Atoms3R" },
        { "Kkdev92.StackChan.Gateway.Capabilities", "Kkdev92.StackChan.Gateway.Providers" },
    };

    [Fact]
    public void Abstractionsは_外部パッケージや他プロジェクトを参照しない()
    {
        var project = Project("Kkdev92.StackChan.Gateway.Abstractions");

        Items(project, "PackageReference").ShouldBeEmpty();
        Items(project, "ProjectReference").ShouldBeEmpty();
        Items(project, "FrameworkReference").ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void 禁止されたプロジェクト参照を持たない(string from, string to)
    {
        Items(Project(from), "ProjectReference")
            .ShouldNotContain(reference => reference.Contains(to, StringComparison.Ordinal));
    }

    [Fact]
    public void Runtimeは_AI関連パッケージを参照しない()
    {
        // Confine the AI framework dependency to AgentFramework.
        var packages = Items(Project("Kkdev92.StackChan.Gateway.Runtime"), "PackageReference");

        packages.ShouldNotContain(package =>
            ForbiddenFrameworks.Any(prefix =>
                package.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void AI関連パッケージを参照できるのは_AgentFrameworkだけである()
    {
        foreach (var (name, _) in AllowedPackages)
        {
            if (name == "Kkdev92.StackChan.Gateway.AgentFramework")
            {
                continue;
            }

            Items(Project(name), "PackageReference").ShouldNotContain(
                package => ForbiddenFrameworks.Any(prefix =>
                    package.StartsWith(prefix, StringComparison.Ordinal)),
                $"{name} が AI 関連パッケージを参照している");
        }
    }

    [Theory]
    [MemberData(nameof(PackagedProjects))]
    public void パッケージ参照は_許可一覧と一致する(string name)
    {
        var packages = Items(Project(name), "PackageReference");

        packages.ShouldBe(AllowedPackages[name], ignoreOrder: true);
    }

    [Fact]
    public void SDKプロジェクトは_Appを参照しない()
    {
        // Preserve dependency direction that allows a gateway to be composed from SDK packages alone.
        foreach (var path in SdkProjectFiles())
        {
            var references = Items(XDocument.Load(path), "ProjectReference");

            references.ShouldNotContain(
                reference =>
                    reference.Contains(@"..\..\app\", StringComparison.OrdinalIgnoreCase) ||
                    reference.Contains(@"src\app", StringComparison.OrdinalIgnoreCase) ||
                    reference.Contains("StackChan.Gateway.App", StringComparison.Ordinal),
                $"{Path.GetFileName(path)} が App を参照している");
        }
    }

    /// <summary>Verifies that the package-reference allowlist covers every SDK project.</summary>
    /// <remarks>
    /// Projects omitted from <c>AllowedPackages</c> would escape dependency inspection, so this list
    /// must match the actual projects. A dedicated test covers dependency-free Abstractions.
    /// </remarks>
    [Fact]
    public void パッケージ参照の許可一覧は_全SDKプロジェクトを網羅する()
    {
        // A dedicated test verifies that Abstractions has no external dependencies.
        string[] checkedElsewhere = ["Kkdev92.StackChan.Gateway.Abstractions"];

        var actual = SdkProjectFiles()
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !checkedElsewhere.Contains(name, StringComparer.Ordinal))
            .ToArray();

        AllowedPackages.Keys.ShouldBe(
            actual,
            ignoreOrder: true,
            "AllowedPackages と SDK プロジェクトの一覧が一致していない。" +
            "新しいプロジェクトを依存関係の検査対象へ追加する必要がある");
    }

    [Fact]
    public void SDKのプロジェクト構成は_基盤パッケージだけである()
    {
        // The SDK contains only contracts and shared infrastructure for turns, protocols, and external
        // services. The reference app composes concrete recognition services and capabilities.
        var expected = new[]
        {
            "Kkdev92.StackChan.Gateway.Abstractions",
            "Kkdev92.StackChan.Gateway.Runtime",
            "Kkdev92.StackChan.Gateway.Protocol.Atoms3R",
            "Kkdev92.StackChan.Gateway.Providers",
            "Kkdev92.StackChan.Gateway.Capabilities",
            "Kkdev92.StackChan.Gateway.AgentFramework",
            "Kkdev92.StackChan.Gateway.Diagnostics",
            "Kkdev92.StackChan.Gateway.TestKit",
        };

        var actual = SdkProjectFiles()
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToArray();

        actual.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void TestKitが参照するSDKプロジェクトは_Abstractionsだけである()
    {
        // TestKit is test-framework independent and returns conformance violations as ordinary values.
        var project = Project("Kkdev92.StackChan.Gateway.TestKit");

        Items(project, "ProjectReference").ShouldBe(
            [@"..\Kkdev92.StackChan.Gateway.Abstractions\Kkdev92.StackChan.Gateway.Abstractions.csproj"]);
        Items(project, "PackageReference").ShouldBeEmpty();
        Items(project, "FrameworkReference").ShouldBeEmpty();
    }

    [Fact]
    public void TestKitの公開APIに_Microsoft固有の型を含めない()
    {
        // Preserve a public API usable from any host or test framework.
        var leaks = Leaks(typeof(ConformanceChecks).Assembly, ["Microsoft."]);

        leaks.ShouldBeEmpty("TestKit の公開 API に Microsoft 固有の型が含まれている: " +
            string.Join(" / ", leaks));
    }

    [Fact]
    public void MEAI001の抑制は_AgentFramework内に限定する()
    {
        // Keep experimental AI API usage from spreading to other SDK packages.
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (!File.ReadAllText(file).Contains(
                "#pragma warning disable MEAI001", StringComparison.Ordinal))
            {
                continue;
            }

            var allowed = Path.Combine(root, "src", "sdk", "Kkdev92.StackChan.Gateway.AgentFramework");

            if (!file.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        offenders.ShouldBeEmpty(
            "MEAI001 の抑制が AgentFramework の外にある: " + string.Join(" / ", offenders));
    }

    [Theory]
    [MemberData(nameof(PublicSurfaces))]
    public void 公開APIに_許可していないフレームワーク型を含めない(
        string name,
        Type marker,
        bool allowAspNet)
    {
        var forbidden = allowAspNet
            ? ForbiddenFrameworks
            : [.. ForbiddenFrameworks, AspNet];

        var leaks = Leaks(marker.Assembly, forbidden);

        leaks.ShouldBeEmpty(
            $"{name} の公開 API に許可されていない型が含まれている: " +
            string.Join(" / ", leaks));
    }

    /// <summary>Collects public API uses of types from prohibited namespaces.</summary>
    private static List<string> Leaks(Assembly assembly, IReadOnlyList<string> forbidden)
    {
        var leaks = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            Inspect(type.BaseType, $"{type.Name} の基底型", leaks, forbidden);

            foreach (var interfaceType in type.GetInterfaces())
            {
                Inspect(interfaceType, $"{type.Name} の実装するインターフェース", leaks, forbidden);
            }

            foreach (var argument in type.GetGenericArguments())
            {
                Inspect(argument, $"{type.Name} の型引数", leaks, forbidden);
            }

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                switch (member)
                {
                    case PropertyInfo property:
                        Inspect(
                            property.PropertyType,
                            $"{type.Name}.{property.Name}",
                            leaks,
                            forbidden);
                        break;

                    case FieldInfo field:
                        Inspect(field.FieldType, $"{type.Name}.{field.Name}", leaks, forbidden);
                        break;

                    case MethodBase method:
                        foreach (var parameter in method.GetParameters())
                        {
                            Inspect(
                                parameter.ParameterType,
                                $"{type.Name}.{method.Name}({parameter.Name})",
                                leaks,
                                forbidden);
                        }

                        if (method is MethodInfo info)
                        {
                            Inspect(
                                info.ReturnType,
                                $"{type.Name}.{method.Name} の戻り値",
                                leaks,
                                forbidden);
                        }

                        break;

                    default:
                        break;
                }
            }
        }

        return leaks;
    }

    [Fact]
    public void Abstractionsが参照するアセンブリは_BCLだけである()
    {
        var referenced = typeof(DeviceId).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? "")
            .ToList();

        referenced.ShouldNotBeEmpty();
        referenced.ShouldAllBe(name =>
            name.StartsWith("System", StringComparison.Ordinal) ||
            name == "netstandard" ||
            name == "mscorlib");
    }

    private static void Inspect(
        Type? type,
        string where,
        List<string> leaks,
        IReadOnlyList<string> forbidden)
    {
        if (type is null)
        {
            return;
        }

        foreach (var candidate in Unwrap(type))
        {
            var name = candidate.FullName ?? candidate.Name;

            if (forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                leaks.Add($"{where}: {name}");
            }
        }
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } element)
        {
            foreach (var inner in Unwrap(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var inner in Unwrap(argument))
                {
                    yield return inner;
                }
            }
        }
    }

    private static IReadOnlyList<string> Items(XDocument project, string itemName) =>
        [.. project.Descendants(itemName)
            .Select(element => element.Attribute("Include")?.Value ?? "")
            .Where(value => value.Length > 0)];

    private static XDocument Project(string name)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "sdk", name, $"{name}.csproj");
        File.Exists(path).ShouldBeTrue($"{path} が見つかりません。");

        return XDocument.Load(path);
    }

    /// <summary>Enumerates every project file under <c>src/sdk</c>.</summary>
    private static IEnumerable<string> SdkProjectFiles() =>
        Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src", "sdk"),
            "*.csproj",
            SearchOption.AllDirectories);

    /// <summary>Verifies that each SDK package README contains the shared footer.</summary>
    /// <remarks>
    /// The package-family description, related-package list, repository link, and prerelease notice are
    /// maintained centrally in <c>eng/PACKAGE-README-FOOTER.md</c>.
    /// </remarks>
    [Fact]
    public void SDKパッケージの_READMEは_共通フッターで終わる()
    {
        var footer = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "eng", "PACKAGE-README-FOOTER.md"))
            .ReplaceLineEndings("\n")
            .TrimEnd();

        footer.ShouldNotBeEmpty();

        var readmes = Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "src", "sdk"),
            "README.md",
            SearchOption.AllDirectories).ToArray();

        readmes.Length.ShouldBe(
            Directory.GetDirectories(Path.Combine(RepositoryRoot(), "src", "sdk")).Length,
            "SDK パッケージごとに README.md が 1 本ずつ必要");

        foreach (var readme in readmes)
        {
            var text = File.ReadAllText(readme).ReplaceLineEndings("\n").TrimEnd();

            text.EndsWith(footer, StringComparison.Ordinal).ShouldBeTrue(
                $"{Path.GetFileName(Path.GetDirectoryName(readme))} の README が " +
                "eng/PACKAGE-README-FOOTER.md で終わっていない");

            // Also confirm that package-specific content appears before the shared footer.
            text[..^footer.Length].Trim().Length.ShouldBeGreaterThan(
                200,
                $"{Path.GetFileName(Path.GetDirectoryName(readme))} の README に本文が無い");
        }
    }

    /// <summary>Finds the repository root from the test output directory.</summary>
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
