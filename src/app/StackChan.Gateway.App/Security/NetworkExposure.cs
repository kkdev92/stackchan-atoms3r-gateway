using System.Globalization;
using System.Net;

namespace StackChan.Gateway.App.Security;

/// <summary>Detects configurations that expose the application to a LAN without authentication.</summary>
/// <remarks>
/// Offline mode allows the token to be omitted for device testing, but listening on all interfaces
/// allows other devices on the same network to operate the gateway. Startup is blocked unless a
/// token is configured or unauthenticated LAN access is explicitly allowed. Configured listeners
/// are never changed automatically.
/// </remarks>
internal static class NetworkExposure
{
    /// <summary>The configuration key that explicitly permits unauthenticated LAN access.</summary>
    public const string AllowUnauthenticatedLanKey =
        "StackChan:Security:AllowUnauthenticatedLan";

    /// <summary>The configuration key that specifies ASP.NET Core listeners.</summary>
    public const string UrlsKey = "urls";

    /// <summary>Returns a warning when the configuration exposes the gateway to a LAN without authentication.</summary>
    /// <param name="urls">Semicolon-delimited listener URLs.</param>
    /// <param name="hasToken">Whether an authentication token is configured.</param>
    /// <param name="allowUnauthenticatedLan">Whether unauthenticated LAN access is explicitly allowed.</param>
    /// <returns><see langword="null"/> for a safe configuration; otherwise a warning with remediation steps.</returns>
    public static string? DescribeRisk(string? urls, bool hasToken, bool allowUnauthenticatedLan)
    {
        if (hasToken || allowUnauthenticatedLan || IsLoopbackOnly(urls))
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            """
            The gateway would be exposed to the LAN without an authentication token ({0}).
            Startup was blocked because other devices on the same network could operate it.

            Choose one of the following options:
              1. Set a 32-character token in the StackChan__Atoms3R__Token environment variable.
              2. Change "Urls" in appsettings.json to http://127.0.0.1:8787.
              3. Set {1} to true only in an isolated test environment.
            """,
            urls,
            AllowUnauthenticatedLanKey);
    }

    /// <summary>Determines whether every listener uses a loopback address.</summary>
    /// <remarks>
    /// An unparseable URL is treated as externally exposed. <c>0.0.0.0</c>, <c>[::]</c>, <c>*</c>,
    /// and <c>+</c> all listen on every interface.
    /// </remarks>
    public static bool IsLoopbackOnly(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            // ASP.NET Core defaults to localhost.
            return true;
        }

        var entries = urls.Split(';', StringSplitOptions.RemoveEmptyEntries |
                                      StringSplitOptions.TrimEntries);

        return entries.Length != 0 && Array.TrueForAll(entries, IsLoopbackUrl);
    }

    private static bool IsLoopbackUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var host = parsed.Host;

        // In ASP.NET Core, * and + represent all interfaces.
        if (host is "*" or "+")
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
