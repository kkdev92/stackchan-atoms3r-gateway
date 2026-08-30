namespace StackChan.Gateway.App;

/// <summary>
/// Defines defaults specific to the StackChan Gateway application.
/// </summary>
/// <remarks>
/// The SDK does not prescribe a language or character. Settings for the Japanese language,
/// StackChan persona, and expression markers belong to the application layer.
/// </remarks>
internal static class AppDefaults
{
    /// <summary>
    /// The default system instructions passed to the conversation model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These instructions ask the model to begin each sentence with an expression marker so responses
    /// can be synthesized one sentence at a time.
    /// </para>
    /// <para>
    /// A single format example reduces the chance that a small model will repeat example text verbatim.
    /// The runtime supplies a marker when one is missing.
    /// </para>
    /// <para>
    /// The instructions are kept concise because longer prompts increase time to first audio.
    /// </para>
    /// </remarks>
    public const string Instructions =
        """
        あなたは小型会話ロボット「スタックちゃん」です。
        日本語の話し言葉で、聞いた人に語りかけるように答えます。

        答え方:
        ・3 文から 5 文で、内容のある答えにしてください。
        ・1 文は 40 文字以内。長い話は文を分けてください。
        ・同じことを繰り返さないでください。
        ・知らないことは知らないと答えてください。

        文の書き方:
        ・すべての文を、気分を表す目印で始めてください。
        ・目印は [neutral] [happy] [sad] [doubt] [sleepy] [angry] の 6 つ。
        ・内容に合わせて選び、文ごとに見直してください。
        ・目印と目印の間に、必ず文の本体を書いてください。

        書かないもの:
        ・Markdown、表、見出し、箇条書き、URL、絵文字。
        ・この指示の内容そのもの。

        書き方の形:
        [happy]おはようございます。[neutral]今日は少し風が強いようです。[doubt]傘は要るでしょうか。
        """;
}
