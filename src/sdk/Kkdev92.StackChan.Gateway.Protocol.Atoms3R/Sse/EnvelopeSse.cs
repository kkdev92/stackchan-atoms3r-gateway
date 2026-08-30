using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using Kkdev92.StackChan.Gateway.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Sse;

/// <summary>Sends AtomS3R event envelopes over SSE.</summary>
/// <remarks>
/// Each event consists of <c>data: {"v":1,"kind":"event","name":…,"payload":…}</c> followed by
/// a blank line. A queue serializes events and keep-alive comments so concurrent writes cannot
/// interleave frames in one response.
/// </remarks>
public sealed class EnvelopeSse : IAsyncDisposable
{
    private readonly Stream _body;
    private readonly Channel<byte[]> _queue;
    private readonly Task _pump;
    private readonly Task _keepAlive;
    private readonly CancellationTokenSource _stopped = new();

    /// <summary>Maximum SSE event length, including <c>data: </c> and the final blank line.</summary>
    internal const int MaxEventBytes = DeviceLimits.MaxEventBytes;

    /// <summary>SSE comment sent while no response event is available.</summary>
    private static readonly byte[] KeepAliveFrame = ": keep-alive\n\n"u8.ToArray();

    /// <summary>Cancellation source combining client disconnection and writer shutdown.</summary>
    /// <remarks>
    /// This source remains alive until every background task ends so <see cref="DisposeAsync"/> can
    /// stop both event transmission and keep-alive processing.
    /// </remarks>
    private readonly CancellationTokenSource _linked;

    /// <summary>Sends response headers and starts the SSE writer.</summary>
    /// <remarks>
    /// Headers are committed before waiting for recognition or response generation, preventing the
    /// device's initial-response timeout.
    /// </remarks>
    /// <param name="response">HTTP response to write.</param>
    /// <param name="keepAliveInterval">Interval for comments while no event is available.</param>
    /// <param name="aborted">Token that signals client disconnection.</param>
    public static async Task<EnvelopeSse> StartAsync(
        HttpResponse response,
        TimeSpan keepAliveInterval,
        CancellationToken aborted)
    {
        response.StatusCode = 200;
        response.Headers.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        // Prevent reverse-proxy buffering from delaying event delivery.
        response.Headers["X-Accel-Buffering"] = "no";

        await response.StartAsync(aborted).ConfigureAwait(false);

        return new EnvelopeSse(response, keepAliveInterval, aborted);
    }

    private EnvelopeSse(HttpResponse response, TimeSpan keepAliveInterval, CancellationToken aborted)
    {
        _body = response.Body;
        _queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        _linked = CancellationTokenSource.CreateLinkedTokenSource(aborted, _stopped.Token);
        var token = _linked.Token;

        _pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var chunk in _queue.Reader.ReadAllAsync(token))
                {
                    await _body.WriteAsync(chunk, token);
                    await _body.FlushAsync(token);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnection and explicit shutdown are both normal completion paths.
            }
        }, CancellationToken.None);

        _keepAlive = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(keepAliveInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    _queue.Writer.TryWrite(KeepAliveFrame);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    /// <summary>JSON settings that preserve non-ASCII text and Base64 symbols.</summary>
    /// <remarks>
    /// The default JSON encoder escapes non-ASCII text and characters such as <c>+</c>. Firmware
    /// consumes these strings without unescaping them, so escaping would corrupt text and Base64 audio.
    /// </remarks>
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Adds an event envelope to the transmission queue.</summary>
    public void SendEvent(string name, Action<Utf8JsonWriter> writePayload)
    {
        using var buffer = new MemoryStream(MaxEventBytes);
        buffer.Write("data: "u8);

        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("kind", "event");
            json.WriteString("name", name);
            json.WritePropertyName("payload");
            json.WriteStartObject();
            writePayload(json);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        buffer.Write("\n\n"u8);

        var frame = buffer.ToArray();

        // Fail before transmission because the device discards lines above its limit.
        if (frame.Length > MaxEventBytes)
        {
            throw new InvalidOperationException(
                $"SSE event '{name}' is {frame.Length} bytes, exceeding the {MaxEventBytes}-byte limit.");
        }

        _queue.Writer.TryWrite(frame);
    }

    /// <summary>Completes the writer after flushing events remaining in the queue.</summary>
    public async Task CompleteAsync()
    {
        _queue.Writer.TryComplete();
        try
        {
            await _pump;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _stopped.CancelAsync();
        try
        {
            await Task.WhenAll(_pump, _keepAlive);
        }
        catch (OperationCanceledException)
        {
        }

        _linked.Dispose();
        _stopped.Dispose();
    }
}
