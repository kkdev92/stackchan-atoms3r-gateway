using System.Text;

namespace Kkdev92.StackChan.Gateway.Runtime.Text;

/// <summary>
/// Converts streamed model output into text suitable for speech.
/// </summary>
/// <remarks>
/// <para>
/// A model can split its response in the middle of a word or tag. Applying <c>Trim</c> or removing
/// patterns from each fragment separately can lose whitespace at fragment boundaries or miss a
/// split <c>&lt;think&gt;</c> tag.
/// </para>
/// <para>
/// This class holds back a suffix that may form part of a pattern and processes the stream as one
/// continuous string. It is not thread-safe; create a new instance for each request.
/// </para>
/// </remarks>
public sealed class SpeechTextSanitizer
{
    /// <summary>The action to take when a removable pattern is detected.</summary>
    private enum PatternKind
    {
        /// <summary>Removes only the pattern.</summary>
        Remove,

        /// <summary>Removes content through the closing tag.</summary>
        StartThinkBlock,

        /// <summary>Removes content through the next whitespace character.</summary>
        StartUrl,
    }

    private const string ThinkClose = "</think>";

    /// <summary>Patterns that trigger removal or a state transition.</summary>
    /// <remarks>Each pattern must be no longer than <see cref="MaxHoldBack"/> + 1 characters.</remarks>
    private static readonly (string Text, PatternKind Kind)[] Patterns =
    [
        ("<think>", PatternKind.StartThinkBlock),
        ("https://", PatternKind.StartUrl),
        ("http://", PatternKind.StartUrl),
        ("```", PatternKind.Remove),
        ("~~~", PatternKind.Remove),
        ("**", PatternKind.Remove),
        ("__", PatternKind.Remove),
    ];

    /// <summary>The maximum number of trailing characters held back because they may form a pattern with the next fragment.</summary>
    /// <remarks>
    /// This is one character shorter than the longest patterns, <c>&lt;/think&gt;</c> and <c>https://</c>.
    /// </remarks>
    private const int MaxHoldBack = 7;

    /// <summary>Characters treated as Markdown list or heading markers at the start of a line.</summary>
    private static readonly char[] LineMarkers = ['#', '-', '*', '+'];

    /// <summary>The maximum recognized Markdown heading level.</summary>
    private const int MaxHeadingLevel = 6;

    private readonly StringBuilder _holdBack = new();
    private readonly StringBuilder _markerCandidate = new();

    private bool _insideThinkBlock;
    private bool _insideUrl;
    private bool _emittedAnything;
    private bool _pendingSpace;
    private bool _atLineStart = true;

    /// <summary>Adds a text fragment and returns the portion confirmed as suitable for speech.</summary>
    /// <returns>Text suitable for speech, or an empty string if no portion is ready.</returns>
    public string Push(string? chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return string.Empty;
        }

        _holdBack.Append(chunk);

        var buffer = _holdBack.ToString();
        var output = new StringBuilder(buffer.Length);
        var consumed = Scan(buffer, output, isFinal: false);

        _holdBack.Clear();
        _holdBack.Append(buffer, consumed, buffer.Length - consumed);

        return output.ToString();
    }

    /// <summary>Finalizes text held back at the end of the stream.</summary>
    /// <returns>The remaining text suitable for speech, or an empty string if none remains.</returns>
    public string Flush()
    {
        var buffer = _holdBack.ToString();
        _holdBack.Clear();

        var output = new StringBuilder(buffer.Length);

        if (buffer.Length > 0)
        {
            Scan(buffer, output, isFinal: true);
        }

        // Speak characters that could not be confirmed as Markdown syntax without trailing whitespace.
        FlushMarkerCandidate(output);

        return output.ToString();
    }

    /// <summary>Scans a buffer and writes confirmed text to <paramref name="output"/>.</summary>
    /// <param name="buffer">The text to scan.</param>
    /// <param name="output">The destination for confirmed text.</param>
    /// <param name="isFinal"><see langword="true"/> to finalize held-back text at the end of the stream.</param>
    /// <returns>The number of characters consumed. The remainder is carried over to the next call.</returns>
    private int Scan(string buffer, StringBuilder output, bool isFinal)
    {
        var index = 0;

        while (index < buffer.Length)
        {
            if (_insideThinkBlock)
            {
                var close = buffer.IndexOf(ThinkClose, index, StringComparison.OrdinalIgnoreCase);

                if (close < 0)
                {
                    // Hold back only the suffix that may be part of a closing tag.
                    return isFinal ? buffer.Length : HoldBackFrom(buffer, index);
                }

                _insideThinkBlock = false;
                index = close + ThinkClose.Length;
                continue;
            }

            if (_insideUrl)
            {
                var space = IndexOfWhiteSpace(buffer, index);

                if (space < 0)
                {
                    // Do not retain URL text between fragments; carry only the in-URL state forward.
                    return buffer.Length;
                }

                _insideUrl = false;
                index = space;
                continue;
            }

            var next = FindNextPattern(buffer, index, out var pattern);

            if (next < 0)
            {
                var safe = isFinal ? buffer.Length : HoldBackFrom(buffer, index);

                Emit(output, buffer.AsSpan(index, safe - index));
                return safe;
            }

            Emit(output, buffer.AsSpan(index, next - index));

            index = next + pattern.Text.Length;

            switch (pattern.Kind)
            {
                case PatternKind.StartThinkBlock:
                    _insideThinkBlock = true;
                    break;

                case PatternKind.StartUrl:
                    // Do not speak URLs; remove content through the next whitespace character.
                    _insideUrl = true;
                    break;

                case PatternKind.Remove:
                default:
                    break;
            }
        }

        return buffer.Length;
    }

    /// <summary>Finds the next removable pattern.</summary>
    /// <remarks>
    /// If multiple patterns match at the same position, their order in <see cref="Patterns"/> takes precedence.
    /// </remarks>
    private static int FindNextPattern(
        string buffer,
        int start,
        out (string Text, PatternKind Kind) pattern)
    {
        var best = -1;
        pattern = default;

        foreach (var candidate in Patterns)
        {
            var found = buffer.IndexOf(candidate.Text, start, StringComparison.OrdinalIgnoreCase);

            if (found >= 0 && (best < 0 || found < best))
            {
                best = found;
                pattern = candidate;
            }
        }

        return best;
    }

    private static int IndexOfWhiteSpace(string buffer, int start)
    {
        for (var i = start; i < buffer.Length; i++)
        {
            if (char.IsWhiteSpace(buffer[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Retains a suffix that could form a pattern and returns the safe boundary.</summary>
    /// <remarks>
    /// The boundary never splits a surrogate pair. A split pair would become replacement characters
    /// during UTF-8 conversion and could not be restored by concatenating later.
    /// </remarks>
    private static int HoldBackFrom(string buffer, int index)
    {
        var safe = Math.Max(index, buffer.Length - MaxHoldBack);

        if (safe > index && safe < buffer.Length && char.IsLowSurrogate(buffer[safe]))
        {
            safe--;
        }

        return safe;
    }

    /// <summary>Writes confirmed text while collapsing consecutive whitespace.</summary>
    /// <remarks>
    /// Trailing whitespace is held until the next non-whitespace character so words at fragment
    /// boundaries do not run together. Only leading and trailing whitespace is removed from the stream.
    /// </remarks>
    private void Emit(StringBuilder output, ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                // A candidate followed by whitespace is Markdown line-start syntax, so remove it.
                _markerCandidate.Clear();

                if (_emittedAnything)
                {
                    _pendingSpace = true;
                }

                // Regular whitespace does not reset the line-start state.
                if (character is '\n' or '\r')
                {
                    _atLineStart = true;
                }

                continue;
            }

            if (IsMarkerCandidate(character))
            {
                // The next character determines whether this is Markdown syntax or regular text.
                _markerCandidate.Append(character);
                continue;
            }

            // Emit characters that did not form Markdown syntax as regular text.
            FlushMarkerCandidate(output);
            EmitPendingSpace(output);

            output.Append(character);
            _emittedAnything = true;
            _atLineStart = false;
        }
    }

    /// <summary>Determines whether a character may be part of Markdown line-start syntax.</summary>
    /// <remarks>
    /// Candidates are one to six <c>#</c> characters or a single <c>-</c>, <c>*</c>, or <c>+</c>
    /// at the start of the stream or immediately after a newline. Characters after regular whitespace
    /// are not candidates, so operators in expressions such as <c>2 * 3 = 6</c> remain intact.
    /// </remarks>
    private bool IsMarkerCandidate(char character)
    {
        if (_markerCandidate.Length == 0)
        {
            return (_atLineStart || !_emittedAnything) && LineMarkers.Contains(character);
        }

        // Only heading markers made of # may contain multiple characters.
        return character == '#' &&
            _markerCandidate[0] == '#' &&
            _markerCandidate.Length < MaxHeadingLevel;
    }

    private void FlushMarkerCandidate(StringBuilder output)
    {
        if (_markerCandidate.Length == 0)
        {
            return;
        }

        EmitPendingSpace(output);

        output.Append(_markerCandidate);
        _markerCandidate.Clear();
        _emittedAnything = true;
    }

    private void EmitPendingSpace(StringBuilder output)
    {
        if (!_pendingSpace)
        {
            return;
        }

        output.Append(' ');
        _pendingSpace = false;
    }
}
