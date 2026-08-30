using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Shouldly;
using Xunit;

namespace Kkdev92.StackChan.Gateway.Runtime.Tests;

/// <summary>
/// Verifies turn processing that combines speech recognition, an agent, and speech synthesis.
/// </summary>
/// <remarks>
/// Fixes event order, count, and failure completion behavior as a device-facing contract.
/// </remarks>
public sealed class TurnRuntimeTests
{
    [Fact]
    public async Task 音声認識_音声応答_完了の順でイベントを返す()
    {
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]こんにちは。"];

        var events = await harness.RunAsync();

        events.Count.ShouldBe(3);
        var transcript = events[0].ShouldBeOfType<TranscriptAvailable>();
        transcript.Text.ShouldBe("こんにちは");
        var reply = events[1].ShouldBeOfType<ReplyAudioAvailable>();

        // Runtime events store text and expression separately without protocol syntax.
        reply.Text.ShouldBe("こんにちは。");
        reply.Expression.ShouldBe(SpeechExpression.Happy);
        reply.Audio.IsCanonical.ShouldBeTrue();
        events[2].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
    }

    [Fact]
    public async Task 音声認識は_1_ターンにつき_1_回だけ呼び出す()
    {
        var harness = new TurnRuntimeHarness();

        await harness.RunAsync();

        harness.SpeechToText.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task エージェントへ_認識テキストとセッション_ID_を渡す()
    {
        var harness = new TurnRuntimeHarness();
        harness.SpeechToText.Result = "今日はいい天気ですね";

        await harness.RunAsync(TurnRuntimeHarness.Request(device: "atoms3r-aabbccddeeff"));

        var request = harness.Agent.Requests.ShouldHaveSingleItem();
        request.UserText.ShouldBe("今日はいい天気ですね");
        request.SessionId.Value.ShouldBe("atoms3r-aabbccddeeff");
        request.DeviceId.Value.ShouldBe("atoms3r-aabbccddeeff");
    }

    [Fact]
    public async Task 文が確定するたびに_音声を合成する()
    {
        var harness = new TurnRuntimeHarness();

        // Split at sentence endings, not at fragment boundaries chosen by the model.
        harness.Agent.Fragments = ["[happy]こんにちは", "。", "[neutral]今日は", "元気です。"];

        var events = await harness.RunAsync();

        var replies = events.OfType<ReplyAudioAvailable>().ToList();
        replies.Select(reply => reply.Text).ShouldBe(
            ["こんにちは。", "今日は元気です。"]);
        replies.Select(reply => reply.Expression).ShouldBe(
            [SpeechExpression.Happy, SpeechExpression.Neutral]);
        harness.TextToSpeech.Calls.ShouldBe(2);
        harness.TextToSpeech.Texts.ShouldBe(["こんにちは。", "今日は元気です。"]);
    }

    [Fact]
    public async Task 文末記号のない応答も_ストリーム終了時に音声合成する()
    {
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]おはよう"];

        var events = await harness.RunAsync();

        var reply = events.OfType<ReplyAudioAvailable>().ShouldHaveSingleItem();
        reply.Text.ShouldBe("おはよう");
        reply.Expression.ShouldBe(SpeechExpression.Happy);
        harness.TextToSpeech.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task 表情マーカーのない文には_内容から推測した表情を適用する()
    {
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["ごめんなさい、わかりません。"];

        var events = await harness.RunAsync();

        var reply = events.OfType<ReplyAudioAvailable>().ShouldHaveSingleItem();
        reply.Text.ShouldBe("ごめんなさい、わかりません。");
        reply.Expression.ShouldBe(SpeechExpression.Sad);

        // Expression markers are not included in text sent to speech synthesis.
        harness.TextToSpeech.Texts.ShouldHaveSingleItem().ShouldBe("ごめんなさい、わかりません。");
    }

    [Theory]
    [InlineData("[neutral]", SpeechExpression.Neutral)]
    [InlineData("[happy]", SpeechExpression.Happy)]
    [InlineData("[sad]", SpeechExpression.Sad)]
    [InlineData("[doubt]", SpeechExpression.Doubt)]
    [InlineData("[sleepy]", SpeechExpression.Sleepy)]
    [InlineData("[angry]", SpeechExpression.Angry)]
    public async Task 表情マーカーを_対応する表情へ変換する(string marker, SpeechExpression expected)
    {
        // Fix the mapping between every marker and SpeechExpression.
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = [marker + "そうですね。おてんきの話でした。"];

        var events = await harness.RunAsync();

        events.OfType<ReplyAudioAvailable>().First().Expression.ShouldBe(expected);
    }

    [Fact]
    public async Task 文中の表情マーカーで_音声応答を分割する()
    {
        // One audio response can carry only one expression, so split wherever the expression changes.
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]あかるいはなし[sad]くらいはなし。おわり。"];

        var events = await harness.RunAsync();
        var replies = events.OfType<ReplyAudioAvailable>().ToList();

        replies.Select(reply => reply.Expression).ShouldContain(SpeechExpression.Happy);
        replies.Select(reply => reply.Expression).ShouldContain(SpeechExpression.Sad);
        replies.ShouldAllBe(reply => !reply.Text.Contains('['));
    }

    [Fact]
    public async Task テキスト入力では_音声認識を呼び出さない()
    {
        var harness = new TurnRuntimeHarness();

        var events = await harness.RunAsync(TurnRuntimeHarness.Request(text: "おはよう"));

        harness.SpeechToText.Calls.ShouldBe(0);
        events[0].ShouldBeOfType<TranscriptAvailable>().Text.ShouldBe("おはよう");
        harness.Agent.Requests.ShouldHaveSingleItem().UserText.ShouldBe("おはよう");
    }

    [Fact]
    public async Task 音声認識結果が空なら_ターンを失敗として終了する()
    {
        var harness = new TurnRuntimeHarness();
        harness.SpeechToText.Result = "   ";

        var events = await harness.RunAsync();

        var failed = events[0].ShouldBeOfType<TurnFailed>();
        failed.Error.Code.ShouldBe(GatewayErrorCode.Unavailable);
        failed.Error.SafeMessage.ShouldBe("speech recognition failed");
        failed.Error.Retryable.ShouldBeTrue();
        events[1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Failed);
        harness.Agent.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task エージェントが本文を返さなければ_ターンを失敗として終了する()
    {
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = [];

        var events = await harness.RunAsync();

        events.OfType<ReplyAudioAvailable>().ShouldBeEmpty();
        var failed = events.OfType<TurnFailed>().ShouldHaveSingleItem();
        failed.Error.SafeMessage.ShouldBe("the model produced no reply");
        failed.Error.Retryable.ShouldBeTrue();
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Failed);
    }

    [Theory]
    [InlineData(GatewayErrorCode.Unavailable, true)]
    [InlineData(GatewayErrorCode.Timeout, true)]
    public async Task 音声認識の失敗は_指定されたエラーコードと再試行可否を保持する(GatewayErrorCode code, bool retryable)
    {
        var harness = new TurnRuntimeHarness();
        harness.SpeechToText.Throws = new ProviderException(code, "stt down", retryable);

        var events = await harness.RunAsync();

        var failed = events[0].ShouldBeOfType<TurnFailed>();
        failed.Error.Code.ShouldBe(code);
        failed.Error.SafeMessage.ShouldBe("stt down");
        failed.Error.Retryable.ShouldBe(retryable);
    }

    [Fact]
    public async Task エージェントの失敗は_認識結果イベントの後に通知する()
    {
        var harness = new TurnRuntimeHarness();
        harness.Agent.Throws = new ProviderException(GatewayErrorCode.Unavailable, "agent down", true);

        var events = await harness.RunAsync();

        events[0].ShouldBeOfType<TranscriptAvailable>();
        events[1].ShouldBeOfType<TurnFailed>().Error.Code.ShouldBe(GatewayErrorCode.Unavailable);
        events[2].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Failed);
    }

    [Fact]
    public async Task 想定外の例外は_internal_エラーとして終了する()
    {
        var harness = new TurnRuntimeHarness();
        harness.SpeechToText.Throws = new InvalidOperationException("boom");

        var events = await harness.RunAsync();

        var failed = events[0].ShouldBeOfType<TurnFailed>();
        failed.Error.Code.ShouldBe(GatewayErrorCode.Internal);
        failed.Error.SafeMessage.ShouldBe("unexpected gateway error");
        failed.Error.Retryable.ShouldBeFalse();
    }

    [Fact]
    public async Task 想定外の例外は_詳細をログへ記録する()
    {
        // Return only a safe predefined message to clients and retain exception details for diagnostics.
        var harness = new TurnRuntimeHarness();
        var thrown = new InvalidOperationException("boom");
        harness.SpeechToText.Throws = thrown;

        await harness.RunAsync();

        harness.Unexpected.ShouldHaveSingleItem().ShouldBeSameAs(thrown);
    }

    [Fact]
    public async Task 想定済みのエラーは_例外ログへ記録しない()
    {
        // Treat unavailable, timeout, and busy as ordinary control outcomes.
        var harness = new TurnRuntimeHarness();
        harness.SpeechToText.Throws = new ProviderException(
            GatewayErrorCode.Unavailable, "stt down", retryable: true);

        await harness.RunAsync();

        harness.Unexpected.ShouldBeEmpty();
    }

    [Fact]
    public async Task internal_エラーとして申告された例外も_詳細をログへ記録する()
    {
        // Internal errors hide their cause from clients, so diagnostics need the exception details.
        var harness = new TurnRuntimeHarness();
        var thrown = new ProviderException(
            GatewayErrorCode.Internal, "unexpected gateway error", retryable: false);
        harness.Agent.Throws = thrown;

        await harness.RunAsync();

        harness.Unexpected.ShouldHaveSingleItem().ShouldBeSameAs(thrown);
    }

    [Fact]
    public async Task 音声合成できない文でも_テキスト応答は返す()
    {
        // Do not lose generated text when synthesis fails for one sentence. If every sentence fails,
        // complete the turn as failed.
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]こんにちは。"];
        harness.TextToSpeech.Throws = new ProviderException(
            GatewayErrorCode.Unavailable, "tts down", true);

        var events = await harness.RunAsync();

        var reply = events.OfType<ReplyAudioAvailable>().ShouldHaveSingleItem();
        reply.Text.ShouldBe("こんにちは。");
        reply.Audio.Samples.Length.ShouldBe(0);
        reply.Audio.IsCanonical.ShouldBeTrue();
    }

    [Fact]
    public async Task すべての文で音声合成に失敗したら_ターンを失敗として終了する()
    {
        // Do not complete a turn with no audio as successful.
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]あさですね。", "[sad]よるですね。"];
        harness.TextToSpeech.Throws = new ProviderException(
            GatewayErrorCode.Unavailable, "tts down", retryable: true);

        var events = await harness.RunAsync();

        // Return generated text regardless of synthesis success.
        var replies = events.OfType<ReplyAudioAvailable>().ToList();
        replies.Count.ShouldBe(2);
        replies.ShouldAllBe(reply => reply.Audio.Samples.IsEmpty);

        // Finally, report the inability to produce audio as an error.
        events.OfType<TurnFailed>().ShouldHaveSingleItem()
            .Error.Code.ShouldBe(GatewayErrorCode.Unavailable);
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Failed);
    }

    [Fact]
    public async Task 一部の文で音声合成に失敗しても_ターンを継続する()
    {
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]あさですね。", "[sad]よるですね。"];

        // Fail synthesis only for the second sentence.
        harness.TextToSpeech.FailFrom = 2;

        var events = await harness.RunAsync();

        var replies = events.OfType<ReplyAudioAvailable>().ToList();
        replies.Count.ShouldBe(2);
        replies[0].Audio.Samples.IsEmpty.ShouldBeFalse();
        replies[1].Audio.Samples.IsEmpty.ShouldBeTrue();

        events.OfType<TurnFailed>().ShouldBeEmpty();
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
    }

    [Fact]
    public async Task デバイス形式に合わない音声は_internal_エラーにする()
    {
        var harness = new TurnRuntimeHarness();
        harness.TextToSpeech.Result = new PcmAudio(new short[] { 1, 2 }, 44100, 1);

        var events = await harness.RunAsync();

        events.OfType<ReplyAudioAvailable>().ShouldBeEmpty();
        events.OfType<TurnFailed>().ShouldHaveSingleItem()
            .Error.Code.ShouldBe(GatewayErrorCode.Internal);
    }

    [Fact]
    public async Task 次の文の音声合成を_前の文の完了前に開始する()
    {
        // Allow the next sentence to be synthesized while the device plays the previous one.
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]あさですね。", "[sad]よるですね。"];

        var gate = new TaskCompletionSource();
        harness.TextToSpeech.Block = gate;

        var running = harness.RunAsync();

        // Processing for the second sentence starts before synthesis of the first completes.
        await TurnRuntimeHarness.WaitUntilAsync(
            () => harness.TextToSpeech.MaxInFlight >= 2, "2 件の音声合成が並行して実行される");

        gate.SetResult();

        var events = await running;

        harness.TextToSpeech.MaxInFlight.ShouldBe(2);

        // Preserve sentence order in response events even with concurrent synthesis.
        var replies = events.OfType<ReplyAudioAvailable>().ToList();
        replies.Count.ShouldBe(2);
        replies[0].Text.ShouldBe("あさですね。");
        replies[1].Text.ShouldBe("よるですね。");
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Completed);
    }

    [Fact]
    public async Task 先読み済みの文は_エージェントが後で失敗しても返す()
    {
        // Do not discard sentences completed before an agent exception, even during synthesis prefetch.
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments =
        [
            "[happy]あさですね。",
            "[sad]よるですね。",
            "[neutral]おやすみなさい、また明日。",
        ];
        harness.Agent.ThrowsAfterFragments = true;

        var events = await harness.RunAsync();

        // Include the second sentence that was being prefetched.
        var replies = events.OfType<ReplyAudioAvailable>().ToList();
        replies.Count.ShouldBe(2);
        replies[0].Text.ShouldBe("あさですね。");
        replies[1].Text.ShouldBe("よるですね。");

        events.OfType<TurnFailed>().ShouldHaveSingleItem();
        events[^1].ShouldBeOfType<TurnCompleted>().Reason.ShouldBe(TurnCompletionReason.Failed);
    }

    [Fact]
    public async Task 終了イベントは_1_度だけ返す()
    {
        var harness = new TurnRuntimeHarness();
        harness.Agent.Fragments = ["[happy]あ。", "[sad]い。", "[doubt]う。"];

        var events = await harness.RunAsync();

        events.OfType<TurnCompleted>().Count().ShouldBe(1);
        events[^1].ShouldBeOfType<TurnCompleted>();
    }
}
