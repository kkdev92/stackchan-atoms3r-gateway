using Kkdev92.StackChan.Gateway.Runtime.Text;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>
/// Verifies rules for converting streaming responses to spoken text.
/// </summary>
/// <remarks>
/// Models can split responses anywhere, so whitespace, tags, URLs, and surrogate pairs must produce
/// the same result even when they cross chunk boundaries.
/// </remarks>
public sealed class SpeechTextSanitizerTests
{
    [Fact]
    public void チャンク境界にある単語間の空白を保持する()
    {
        // Trimming each chunk separately would lose spaces between English words.
        var result = Run("The answer ", "is 42.");

        result.ShouldBe("The answer is 42.");
    }

    [Fact]
    public void 空白だけのチャンクも_単語の区切りとして扱う()
    {
        Run("Hello", " ", "world").ShouldBe("Hello world");
    }

    [Fact]
    public void 応答全体の先頭と末尾の空白を除去する()
    {
        Run("  ", "こんにちは。", "  ").ShouldBe("こんにちは。");
    }

    [Fact]
    public void 連続する空白を_1_文字へ正規化する()
    {
        Run("a  ", "   b").ShouldBe("a b");
    }

    [Fact]
    public void 改行も空白として正規化する()
    {
        Run("一行目\n", "\n二行目").ShouldBe("一行目 二行目");
    }

    [Fact]
    public void 推論ブロックを除去する()
    {
        Run("<think>内部の考え</think>回答です。").ShouldBe("回答です。");
    }

    [Fact]
    public void 開始タグがチャンクをまたいでも_推論ブロックを除去する()
    {
        // Returning tag fragments as text could expose internal reasoning as speech.
        Run("<thi", "nk>内部の考え</think>回答です。").ShouldBe("回答です。");
    }

    [Fact]
    public void 閉じタグがチャンクをまたいでも_推論ブロックを除去する()
    {
        Run("<think>考え中", "</thi", "nk>答えます。").ShouldBe("答えます。");
    }

    [Fact]
    public void 推論ブロックが_1_文字ずつ届いても除去する()
    {
        var input = "<think>秘密</think>公開";
        var chunks = input.Select(character => character.ToString()).ToArray();

        Run(chunks).ShouldBe("公開");
    }

    [Fact]
    public void 閉じタグがないまま終了したら_推論内容を破棄する()
    {
        // Do not speak incomplete reasoning content when a response ends early.
        Run("回答の前に", "<think>まだ考えている").ShouldBe("回答の前に");
    }

    [Fact]
    public void 複数の推論ブロックを除去する()
    {
        Run("<think>一つ目</think>A", "<think>二つ目</think>B").ShouldBe("AB");
    }

    [Fact]
    public void 大文字を含む推論タグも除去する()
    {
        Run("<THINK>考え</THINK>答え").ShouldBe("答え");
    }

    [Fact]
    public void URL_を除去する()
    {
        Run("詳細は https://example.com/a/b です").ShouldBe("詳細は です");
    }

    [Fact]
    public void URL_がチャンクをまたいでも除去する()
    {
        Run("見て ht", "tps://exa", "mple.com/x です").ShouldBe("見て です");
    }

    [Fact]
    public void 応答末尾の_URL_も除去する()
    {
        Run("こちら http://example.com/very/long/path").ShouldBe("こちら");
    }

    [Theory]
    [InlineData("**強調**です", "強調です")]
    [InlineData("```コード```", "コード")]
    [InlineData("__下線__", "下線")]
    public void 行内の_Markdown_記法を除去する(string input, string expected)
    {
        Run(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("# 見出し", "見出し")]
    [InlineData("### 見出し", "見出し")]
    [InlineData("- 箇条書き", "箇条書き")]
    [InlineData("* 箇条書き", "箇条書き")]
    [InlineData("+ 箇条書き", "箇条書き")]
    public void 行頭の_Markdown_記法を除去する(string input, string expected)
    {
        Run(input).ShouldBe(expected);
    }

    [Fact]
    public void 行頭以外の記号は本文として残す()
    {
        // Minus signs and asterisks can be part of regular text.
        Run("気温は-5度です").ShouldBe("気温は-5度です");
    }

    [Fact]
    public void Markdown_記法の条件を満たさない文字列は本文として残す()
    {
        // Do not treat a symbol as Markdown syntax unless whitespace follows it.
        Run("-5度から+3度まで").ShouldBe("-5度から+3度まで");
    }

    [Theory]
    [InlineData("2 * 3 = 6", "2 * 3 = 6")]
    [InlineData("5 度 - 10 度です", "5 度 - 10 度です")]
    [InlineData("A + B", "A + B")]
    public void 行内の空白に続く記号は_箇条書きとして扱わない(string input, string expected)
    {
        // Removing inline symbols would corrupt formulas and ranges.
        Run(input).ShouldBe(expected);
    }

    [Fact]
    public void 改行直後の記号は_箇条書きとして除去する()
    {
        Run("一覧です。\n- 一つ目\n- 二つ目").ShouldBe("一覧です。 一つ目 二つ目");
    }

    [Fact]
    public void 見出し記号は_6_段まで除去する()
    {
        // Seven or more # characters are not a Markdown heading, so preserve them as text.
        Run("###### 六段").ShouldBe("六段");
        Run("####### 見出しではない").ShouldBe("####### 見出しではない");
    }

    [Fact]
    public void 記号だけで終了したら_本文として返す()
    {
        Run("答えは", " -").ShouldBe("答えは -");
    }

    [Fact]
    public void 通常の日本語本文を変更しない()
    {
        Run("こんにちは。", "今日は良い天気ですね。")
            .ShouldBe("こんにちは。今日は良い天気ですね。");
    }

    [Fact]
    public void サロゲートペアがチャンクをまたいでも保持する()
    {
        var emoji = "😀";

        // Feed the high and low surrogate in separate chunks.
        var result = Run(emoji[..1].ToString(), emoji[1..].ToString());

        result.ShouldBe(emoji);
    }

    [Fact]
    public void Push_の戻り値を_上位サロゲートで終わらせない()
    {
        // Writing an incomplete surrogate to SSE produces a UTF-8 replacement character that cannot be recovered.
        var sanitizer = new SpeechTextSanitizer();
        var input = string.Concat(Enumerable.Repeat("あ😀", 40));

        foreach (var character in input)
        {
            var piece = sanitizer.Push(character.ToString());

            AssertNoLoneSurrogate(piece);
        }

        AssertNoLoneSurrogate(sanitizer.Flush());

        static void AssertNoLoneSurrogate(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            char.IsHighSurrogate(text[^1]).ShouldBeFalse(
                $"末尾が上位サロゲートのまま返されました: {text}");

            char.IsLowSurrogate(text[0]).ShouldBeFalse(
                $"先頭が下位サロゲートのまま返されました: {text}");
        }
    }

    [Fact]
    public void 入力がなければ空文字列を返す()
    {
        Run().ShouldBeEmpty();
        Run(string.Empty).ShouldBeEmpty();
    }

    [Fact]
    public void 空白だけなら空文字列を返す()
    {
        Run("   ", "\n").ShouldBeEmpty();
    }

    [Fact]
    public void Push_の戻り値を連結した結果は_チャンク分割に依存しない()
    {
        const string Original =
            "はい。<think>これは内部の推論です</think>今日は **晴れ** です。詳細は https://example.com をどうぞ。";

        var expected = Run(Original);

        // Results are identical for splits at one-, two-, or three-character intervals.
        foreach (var size in (int[])[1, 2, 3, 5, 8, 13])
        {
            Run(Split(Original, size)).ShouldBe(expected, $"分割幅 {size} で結果が一致しません。");
        }
    }

    private static string Run(params string[] chunks)
    {
        var sanitizer = new SpeechTextSanitizer();
        var output = new System.Text.StringBuilder();

        foreach (var chunk in chunks)
        {
            output.Append(sanitizer.Push(chunk));
        }

        output.Append(sanitizer.Flush());

        return output.ToString();
    }

    private static string[] Split(string text, int size) =>
        [.. Enumerable
            .Range(0, (text.Length + size - 1) / size)
            .Select(i => text.Substring(i * size, Math.Min(size, text.Length - (i * size))))];
}
