using System.Globalization;
using Kkdev92.StackChan.Gateway.Abstractions;

namespace StackChan.Capability.Time;

/// <summary>
/// Returns the current date and time in spoken Japanese.
/// </summary>
/// <remarks>
/// <para>
/// This class is independent of agent-specific types. It can be registered as a capability or called directly.
/// </para>
/// <para>
/// The result uses a spoken date-and-time format instead of a machine-oriented ISO format.
/// </para>
/// </remarks>
/// <param name="timeProvider">The source of the current time.</param>
public sealed class TimeCapability(TimeProvider timeProvider) : ICapability
{
    private static readonly CultureInfo JapaneseCulture = new("ja-JP");

    /// <summary>Returns the local date and time.</summary>
    [CapabilityAction(
        "get_current_time",
        "現在の日付と時刻を取得します。時刻を聞かれたら必ずこれを使ってください。",
        IsReadOnly = true,
        Triggers = ["何時", "なんじ", "時刻", "何日", "なんにち", "日付", "曜日"])]
    public string GetCurrentTime() =>
        timeProvider.GetLocalNow().ToString("yyyy年M月d日 dddd HH時mm分", JapaneseCulture);
}
