using System.Text;

namespace Kkdev92.StackChan.Gateway.Runtime.Text;

/// <summary>
/// Assembles streamed text fragments into sentences that can be spoken.
/// </summary>
/// <remarks>
/// Each sentence is synthesized separately and sent as the text for the corresponding
/// <c>reply.audio</c> event. Expression markers such as <c>[happy]</c> remain in the sentence but
/// can be removed with <see cref="StripMarkers"/> before speech synthesis.
/// </remarks>
public sealed class SentenceAssembler
{
    /// <summary>Characters treated as sentence terminators in Japanese and English.</summary>
    private static readonly char[] Terminators = ['。', '．', '！', '？', '!', '?', '\n'];

    /// <summary>
    /// The number of characters after which text without a terminator is split.
    /// </summary>
    private const int MaxSentenceChars = 60;

    private readonly StringBuilder _pending = new();

    /// <summary>Adds a text fragment and returns any newly completed sentences.</summary>
    public IEnumerable<string> Push(string fragment)
    {
        _pending.Append(fragment);

        while (true)
        {
            var text = _pending.ToString();
            var cut = FindCut(text);
            if (cut < 0)
            {
                yield break;
            }

            var sentence = text[..cut].Trim();
            _pending.Clear();
            _pending.Append(text, cut, text.Length - cut);

            if (sentence.Length > 0)
            {
                yield return sentence;
            }
        }
    }

    /// <summary>Returns pending text as the final sentence and clears the internal buffer.</summary>
    public string? Flush()
    {
        var sentence = _pending.ToString().Trim();
        _pending.Clear();
        return sentence.Length > 0 ? sentence : null;
    }

    /// <summary>Removes expression markers and returns text suitable for speech synthesis.</summary>
    public static string StripMarkers(string sentence)
    {
        var text = sentence;
        foreach (var marker in ExpressionMarkers.All)
        {
            text = text.Replace(marker, "", StringComparison.Ordinal);
        }
        return text.Trim();
    }

    /// <summary>
    /// Returns the next sentence boundary, or -1 when the text cannot yet be split.
    /// </summary>
    private static int FindCut(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (Array.IndexOf(Terminators, text[i]) >= 0)
            {
                return i + 1;
            }
        }

        if (text.Length >= MaxSentenceChars)
        {
            // Prefer the last comma before the limit when splitting a long sentence.
            var comma = text.LastIndexOfAny(['、', ','], MaxSentenceChars - 1);
            return comma > 0 ? comma + 1 : MaxSentenceChars;
        }

        return -1;
    }
}
