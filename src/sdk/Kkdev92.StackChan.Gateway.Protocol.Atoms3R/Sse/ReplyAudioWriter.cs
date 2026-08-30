using System.Runtime.InteropServices;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Sse;

/// <summary>
/// Sends synthesized audio as AtomS3R <c>reply.audio</c> events.
/// </summary>
/// <remarks>
/// Audio is split into 2,048 samples per event, and <c>text</c> appears only in the first event for
/// each sentence. This class also assigns sequence numbers and device expression markers such as
/// <c>[happy]</c>.
/// </remarks>
/// <param name="sse">Event destination.</param>
internal sealed class ReplyAudioWriter(EnvelopeSse sse)
{
    /// <summary>Maximum samples stored in one event.</summary>
    private const int ChunkSamples = 2048;

    private long _sequence;

    /// <summary>Sends synthesized audio for one sentence.</summary>
    public void Write(ReplyAudioAvailable reply)
    {
        var samples = reply.Audio.Samples.Span;

        // Enforce the device limit independently of upstream segmentation settings.
        var text = DeviceText.Clamp(MarkerFor(reply.Expression) + reply.Text);

        if (samples.Length == 0)
        {
            // Deliver text with empty PCM when synthesis fails for a sentence.
            Send(_sequence++, text, [], last: false);
            return;
        }

        for (var at = 0; at < samples.Length; at += ChunkSamples)
        {
            var length = Math.Min(ChunkSamples, samples.Length - at);

            Send(
                _sequence++,
                at == 0 ? text : null,
                samples.Slice(at, length),
                last: false);
        }
    }

    /// <summary>
    /// Sends the final event in the stream.
    /// </summary>
    /// <remarks>
    /// An audio-free event with <c>last=true</c> tells the device to commit the final sentence.
    /// </remarks>
    public void WriteFinal() => Send(_sequence, text: null, [], last: true);

    /// <summary>
    /// Indicates whether at least one <c>reply.audio</c> event has been sent.
    /// </summary>
    /// <remarks>
    /// A final event is required to commit buffered text even when processing fails after audio was sent.
    /// </remarks>
    public bool HasAudio => _sequence > 0;

    /// <summary>
    /// Converts an expression to a marker recognized by the device.
    /// </summary>
    /// <remarks>
    /// Unknown markers appear as ordinary text, so only spellings defined here are emitted.
    /// </remarks>
    private static string MarkerFor(SpeechExpression expression) =>
        expression switch
        {
            SpeechExpression.Happy => "[happy]",
            SpeechExpression.Sad => "[sad]",
            SpeechExpression.Doubt => "[doubt]",
            SpeechExpression.Sleepy => "[sleepy]",
            SpeechExpression.Angry => "[angry]",
            _ => "[neutral]",
        };

    private void Send(long sequence, string? text, ReadOnlySpan<short> pcm, bool last)
    {
        var base64 = pcm.Length == 0
            ? ""
            : Convert.ToBase64String(MemoryMarshal.AsBytes(pcm));

        sse.SendEvent("reply.audio", json =>
        {
            json.WriteNumber("seq", sequence);
            if (text is not null)
            {
                json.WriteString("text", text);
            }

            json.WriteNumber("rate", PcmAudio.CanonicalSampleRate);
            json.WriteString("pcm", base64);
            json.WriteBoolean("last", last);
        });
    }
}
