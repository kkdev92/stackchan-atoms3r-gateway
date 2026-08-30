using Kkdev92.StackChan.Gateway.Providers.Text;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Providers.Tests;

/// <summary>
/// Verifies the rules that strip non-speech annotations from a recognition result.
/// </summary>
/// <remarks>
/// The bracketed annotations whisper-server returns for silence, noise, and music must not
/// reach the conversation input unchanged.
/// </remarks>
public sealed class NonSpeechAnnotationsTests
{
    [Theory]
    // Representative annotations returned for silence, faint noise, and single tones.
    [InlineData("(音楽)\n")]
    // Annotations returned when no_speech_thold is lowered.
    [InlineData("[音楽]\n")]
    // Annotations whose content varies are stripped by bracket shape, not by wording.
    [InlineData("(シャキャラクター)\n")]
    [InlineData("（拍手）")]
    [InlineData("【音楽】")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("♪")]
    [InlineData("  (音楽)  \n\n")]
    public void 注記だけなら_空文字列を返す(string heard)
    {
        NonSpeechAnnotations.Strip(heard).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("こんにちは。", "こんにちは。")]
    [InlineData("いま何時ですか", "いま何時ですか")]
    [InlineData("(音楽) こんにちは。", "こんにちは。")]
    [InlineData("こんにちは。(拍手)", "こんにちは。")]
    [InlineData("今日は「いい天気」ですね。", "今日は「いい天気」ですね。")]
    public void 発話テキストは保持する(string heard, string expected)
    {
        NonSpeechAnnotations.Strip(heard).ShouldBe(expected);
    }

    [Fact]
    public void 閉じ括弧のない注記は_末尾まで除去する()
    {
        // A truncated annotation must not reach the conversation input.
        NonSpeechAnnotations.Strip("(音楽").ShouldBeEmpty();
    }

    [Fact]
    public void 注記の除去後に句読点だけ残ったら_空文字列を返す()
    {
        NonSpeechAnnotations.Strip("(音楽)。").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void 空の入力は_空文字列のまま返す(string? heard)
    {
        NonSpeechAnnotations.Strip(heard).ShouldBeEmpty();
    }
}
