using System.Diagnostics;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Sse;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Endpoints;

/// <summary>Registers the AtomS3R conversation endpoint.</summary>
/// <remarks>
/// <c>POST /v1/converse</c> receives WAV or text input and returns turn progress as SSE event
/// envelopes. Closing the client connection cancels the entire turn, including recognition, agent
/// processing, and synthesis.
/// </remarks>
public static class ConverseEndpoint
{
    /// <summary>Adds <c>POST /v1/converse</c> to the endpoints.</summary>
    public static IEndpointConventionBuilder MapStackChanAtoms3RConverse(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost("/v1/converse", HandleAsync);
    }

    private static async Task HandleAsync(
        HttpContext context,
        ITurnRuntime runtime,
        IOptions<Atoms3ROptions> options,
        ILoggerFactory loggerFactory)
    {
        var logger = SafeLogger.Create(loggerFactory, "StackChan.Converse");
        var settings = options.Value;

        // Combine connection and application cancellation so host shutdown releases downstream work.
        // A minimal host can operate without an application lifetime service.
        var lifetime = context.RequestServices.GetService<IHostApplicationLifetime>();

        using var stopping = lifetime is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted, lifetime.ApplicationStopping);

        var aborted = stopping?.Token ?? context.RequestAborted;

        using var body = new MemoryStream();
        var request = await ConverseRequestReader.ReadAsync(
            context, settings, body, logger, aborted);

        if (request is null)
        {
            // ReadAsync has already written the error response.
            return;
        }

        var device = request.Device;

        // Do not log the token, request body, or user utterance.
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["device_id"] = device.DeviceId.Value,
            ["boot_id"] = device.BootId,
            ["conversation_id"] = device.ConversationId,
            ["session_id"] = request.SessionId.Value,
        });

        var started = Stopwatch.GetTimestamp();
        // Include the device identifier in the message for loggers that do not render scopes.
        logger.LogInformation(
            "turn accepted. stage={Stage} device={Device} bytes={Bytes} samples={Samples} mode={Mode}",
            "accepted",
            device.DeviceId.Value,
            body.Length,
            request.Audio.Samples.Length,
            request.UserText is null ? "audio" : "text");

        // Send headers before downstream processing to avoid the initial-response timeout.
        await using var sse = await EnvelopeSse.StartAsync(
            context.Response,
            TimeSpan.FromSeconds(settings.KeepAliveIntervalSeconds),
            aborted);

        sse.SendEvent("conversation.started",
            json => json.WriteString("conversation_id", device.ConversationId));

        var audio = new ReplyAudioWriter(sse);

        try
        {
            await foreach (var turnEvent in runtime.ExecuteAsync(request, aborted))
            {
                switch (turnEvent)
                {
                    case TranscriptAvailable transcript:
                        // Log only the character count, not the user's utterance.
                        logger.LogInformation(
                            "turn stage={Stage} device={Device} chars={Chars} duration_ms={Duration}",
                            "transcript",
                            device.DeviceId.Value,
                            transcript.Text.Length,
                            Elapsed(started));

                        var spoken = DeviceText.Clamp(transcript.Text);

                        if (spoken.Length != transcript.Text.Length)
                        {
                            logger.LogWarning(
                                "turn stage={Stage} device={Device} text_truncated_to_bytes={Bytes}",
                                "transcript",
                                device.DeviceId.Value,
                                DeviceText.MaxBytes);
                        }

                        sse.SendEvent("conversation.text", json =>
                        {
                            json.WriteString("text", spoken);
                            json.WriteBoolean("final", true);
                        });
                        break;

                    case ReplyAudioAvailable reply:
                        // Warn when synthesis produced empty PCM so the failure remains diagnosable.
                        if (reply.Audio.Samples.IsEmpty)
                        {
                            logger.LogWarning(
                                "turn stage={Stage} device={Device} samples={Samples} voiced={Voiced} duration_ms={Duration}",
                                "reply",
                                device.DeviceId.Value,
                                0,
                                false,
                                Elapsed(started));
                        }
                        else
                        {
                            logger.LogInformation(
                                "turn stage={Stage} device={Device} samples={Samples} voiced={Voiced} duration_ms={Duration}",
                                "reply",
                                device.DeviceId.Value,
                                reply.Audio.Samples.Length,
                                true,
                                Elapsed(started));
                        }

                        audio.Write(reply);
                        break;

                    case TurnFailed failed:
                        // TurnCompleted is sufficient for cancellation caused by normal disconnection.
                        if (failed.Error.Code != GatewayErrorCode.Cancelled)
                        {
                            logger.LogWarning(
                                "turn stage={Stage} device={Device} code={Code} retryable={Retryable} duration_ms={Duration}",
                                "failed",
                                device.DeviceId.Value,
                                failed.Error.Code,
                                failed.Error.Retryable,
                                Elapsed(started));
                        }

                        sse.SendEvent("error.raised", json =>
                        {
                            json.WriteString("code", WireCode(failed.Error.Code));
                            json.WriteString("message", failed.Error.SafeMessage);
                            json.WriteBoolean("retryable", failed.Error.Retryable);
                        });
                        break;

                    case TurnCompleted completed:
                        logger.LogInformation(
                            "turn stage={Stage} device={Device} reason={Reason} duration_ms={Duration}",
                            "completed",
                            device.DeviceId.Value,
                            completed.Reason,
                            Elapsed(started));

                        WriteCompletion(sse, audio, completed.Reason, aborted);
                        break;

                    default:
                        break;
                }
            }

            await sse.CompleteAsync();
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // Do not send conversation.finished to a disconnected client.
            logger.LogInformation(
                "turn stage={Stage} device={Device} duration_ms={Duration}",
                "cancelled",
                device.DeviceId.Value,
                Elapsed(started));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "turn stage={Stage} device={Device} duration_ms={Duration}",
                "internal",
                device.DeviceId.Value,
                Elapsed(started));

            sse.SendEvent("error.raised", json =>
            {
                json.WriteString("code", "internal");
                json.WriteString("message", "unexpected gateway error");
                json.WriteBoolean("retryable", false);
            });
            sse.SendEvent("conversation.finished", json => json.WriteString("reason", "failed"));
            await sse.CompleteAsync();
        }
    }

    /// <summary>Sends the turn completion reason as a protocol event.</summary>
    /// <remarks>
    /// If audio was sent, a final event with empty PCM and <c>last=true</c> commits the device buffer
    /// before <c>conversation.finished</c> is sent.
    /// </remarks>
    private static void WriteCompletion(
        EnvelopeSse sse,
        ReplyAudioWriter audio,
        TurnCompletionReason reason,
        CancellationToken aborted)
    {
        switch (reason)
        {
            case TurnCompletionReason.Completed:
                audio.WriteFinal();
                sse.SendEvent(
                    "conversation.finished",
                    json => json.WriteString("reason", "completed"));
                break;

            case TurnCompletionReason.Failed:
                if (audio.HasAudio)
                {
                    audio.WriteFinal();
                }

                sse.SendEvent(
                    "conversation.finished",
                    json => json.WriteString("reason", "failed"));
                break;

            case TurnCompletionReason.Cancelled:
                if (!aborted.IsCancellationRequested)
                {
                    sse.SendEvent(
                        "conversation.finished",
                        json => json.WriteString("reason", "cancelled"));
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Converts an error code to its protocol-defined string.</summary>
    private static string WireCode(GatewayErrorCode code) => code switch
    {
        GatewayErrorCode.Unavailable => "unavailable",
        GatewayErrorCode.Timeout => "timeout",
        GatewayErrorCode.Busy => "busy",
        GatewayErrorCode.Cancelled => "cancelled",
        _ => "internal",
    };

    /// <summary>Returns milliseconds elapsed since the start timestamp.</summary>
    private static long Elapsed(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

}
