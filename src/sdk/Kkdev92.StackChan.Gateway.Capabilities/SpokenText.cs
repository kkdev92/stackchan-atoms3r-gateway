using System.Globalization;

namespace Kkdev92.StackChan.Gateway.Capabilities;

/// <summary>
/// Converts capability results to text suitable for speech.
/// </summary>
/// <remarks>
/// Callers format language- or region-dependent values such as dates and times.
/// </remarks>
public static class SpokenText
{
    /// <summary>
    /// Converts a number to text suitable for speech.
    /// </summary>
    /// <remarks>
    /// The result always uses a period as the decimal separator and omits digit grouping and
    /// exponential notation. It removes trailing zeros, so <c>28.30</c> becomes <c>28.3</c> and
    /// <c>30.0</c> becomes <c>30</c>. Midpoint values use the same to-even rounding as
    /// <see cref="Math.Round(double, int)"/>.
    /// </remarks>
    /// <param name="value">Number to convert.</param>
    /// <param name="digits">Maximum fractional digits to retain; defaults to <c>1</c>.</param>
    /// <returns>The number formatted independently of the current culture.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="digits"/> is outside 0 through 15.</exception>
    public static string Number(double value, int digits = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digits);

        // Fix the decimal separator so speech output does not depend on the process culture.
        return Math.Round(value, digits)
            .ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
    }
}
