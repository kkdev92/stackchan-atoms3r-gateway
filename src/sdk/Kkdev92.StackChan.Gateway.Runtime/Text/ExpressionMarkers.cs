using Kkdev92.StackChan.Gateway.Abstractions;

namespace Kkdev92.StackChan.Gateway.Runtime.Text;

/// <summary>Parses or supplies expression markers at the beginning of a sentence.</summary>
/// <remarks>
/// The device uses markers such as <c>[happy]</c> to select an expression for each sentence.
/// When the agent omits a marker, this class makes a conservative inference from punctuation and
/// common phrases. Existing markers are left unchanged.
/// </remarks>
public static class ExpressionMarkers
{
    /// <summary>Maps expressions to their protocol markers.</summary>
    private static readonly (SpeechExpression Expression, string Marker)[] Map =
    [
        (SpeechExpression.Neutral, "[neutral]"),
        (SpeechExpression.Happy,   "[happy]"),
        (SpeechExpression.Sad,     "[sad]"),
        (SpeechExpression.Doubt,   "[doubt]"),
        (SpeechExpression.Sleepy,  "[sleepy]"),
        (SpeechExpression.Angry,   "[angry]"),
    ];

    /// <summary>All expression markers recognized by the device.</summary>
    public static readonly string[] All = [.. Map.Select(pair => pair.Marker)];

    /// <summary>Converts a marker at the beginning of a sentence to an expression.</summary>
    /// <remarks>
    /// If the sentence has no recognized marker, this method returns
    /// <see cref="SpeechExpression.Neutral"/> and assigns the entire sentence to
    /// <paramref name="spoken"/>. Unknown markers such as <c>[foo]</c> are treated as regular text.
    /// </remarks>
    /// <param name="sentence">The sentence to parse.</param>
    /// <param name="expression">The parsed expression, or neutral when no marker is present.</param>
    /// <param name="spoken">The text without the expression marker.</param>
    /// <returns><see langword="true"/> if the sentence began with a recognized expression marker.</returns>
    public static bool TryRead(
        string sentence,
        out SpeechExpression expression,
        out string spoken)
    {
        ArgumentNullException.ThrowIfNull(sentence);

        foreach (var (candidate, marker) in Map)
        {
            if (sentence.StartsWith(marker, StringComparison.Ordinal))
            {
                expression = candidate;
                spoken = sentence[marker.Length..];

                return true;
            }
        }

        expression = SpeechExpression.Neutral;
        spoken = sentence;

        return false;
    }

    /// <summary>Japanese apologies and negative phrases that imply a sad expression.</summary>
    private static readonly string[] SorryWords =
        ["ごめん", "すみません", "申し訳", "残念", "わかりません", "分かりません",
         "できません", "知りません"];

    /// <summary>Returns whether a sentence begins with a recognized expression marker.</summary>
    public static bool HasMarker(string sentence)
    {
        foreach (var marker in All)
        {
            if (sentence.StartsWith(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds a marker inferred from the content when the sentence has no expression marker.
    /// </summary>
    public static string Ensure(string sentence)
    {
        if (sentence.Length == 0 || HasMarker(sentence))
        {
            return sentence;
        }

        return MarkerFor(Guess(sentence)) + sentence;
    }

    /// <summary>
    /// Returns the protocol marker for an expression.
    /// </summary>
    private static string MarkerFor(SpeechExpression expression)
    {
        foreach (var (candidate, marker) in Map)
        {
            if (candidate == expression)
            {
                return marker;
            }
        }

        // Detect a missing map entry when a new expression is added.
        throw new ArgumentOutOfRangeException(nameof(expression), expression, null);
    }


    private static SpeechExpression Guess(string sentence)
    {
        // Remove closing brackets so punctuation immediately before them can be recognized.
        var tail = sentence.TrimEnd(']', ')', '」', '』', '"', '\'', ' ', '。');

        if (tail.EndsWith('？') || tail.EndsWith('?'))
        {
            return SpeechExpression.Doubt;
        }

        if (tail.EndsWith('！') || tail.EndsWith('!'))
        {
            return SpeechExpression.Happy;
        }

        foreach (var word in SorryWords)
        {
            if (sentence.Contains(word, StringComparison.Ordinal))
            {
                return SpeechExpression.Sad;
            }
        }

        return SpeechExpression.Neutral;
    }
}
