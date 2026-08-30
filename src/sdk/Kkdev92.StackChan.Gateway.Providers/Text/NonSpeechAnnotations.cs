using System.Text;

namespace Kkdev92.StackChan.Gateway.Providers.Text;

/// <summary>
/// Removes annotations for non-speech sounds from speech-recognition output.
/// </summary>
/// <remarks>
/// Recognition engines such as whisper.cpp may return music or noise as bracketed text such as
/// <c>(music)</c> or <c>[noise]</c>. This helper removes matching bracketed sections and standalone
/// music symbols without depending on annotation vocabulary. Do not use it when bracketed text
/// should be preserved as speech.
/// </remarks>
public static class NonSpeechAnnotations
{
    private static readonly (char Open, char Close)[] Brackets =
    [
        ('(', ')'),
        ('（', '）'),
        ('[', ']'),
        ('［', '］'),
        ('【', '】'),
    ];

    private static readonly char[] MusicMarks = ['♪', '♫', '♬'];

    /// <summary>
    /// Removes non-speech annotations from recognition output.
    /// </summary>
    /// <param name="text">Recognition output; may be <see langword="null"/>.</param>
    /// <returns>
    /// Text without annotations, or an empty string when only white space, annotations,
    /// punctuation, or symbols remain.
    /// </returns>
    public static string Strip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var kept = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var c = text[index];

            if (Closing(c) is { } close)
            {
                // Remove an unclosed annotation through the end so a truncated marker is not spoken.
                var end = text.IndexOf(close, index + 1);
                index = end < 0 ? text.Length : end + 1;

                continue;
            }

            if (Array.IndexOf(MusicMarks, c) >= 0)
            {
                index++;

                continue;
            }

            kept.Append(c);
            index++;
        }

        // Treat punctuation or symbols left around annotations as no speech.
        var spoken = kept.ToString().Trim();

        return HasContent(spoken) ? spoken : "";
    }

    private static char? Closing(char c)
    {
        foreach (var (open, close) in Brackets)
        {
            if (c == open)
            {
                return close;
            }
        }

        return null;
    }

    private static bool HasContent(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c))
            {
                return true;
            }
        }

        return false;
    }
}
