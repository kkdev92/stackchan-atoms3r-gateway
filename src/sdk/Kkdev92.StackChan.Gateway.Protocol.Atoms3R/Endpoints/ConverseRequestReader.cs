using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kkdev92.StackChan.Gateway.Abstractions;
using Kkdev92.StackChan.Gateway.Abstractions.Turns;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kkdev92.StackChan.Gateway.Protocol.Atoms3R.Endpoints;

/// <summary>Validates an AtomS3R HTTP request and converts it to a turn request.</summary>
/// <remarks>
/// The token, identifiers, body size, text size, and WAV format are validated before SSE begins.
/// A validation failure writes an HTTP error response without starting SSE.
/// </remarks>
internal static class ConverseRequestReader
{
    /// <summary>Reads a device request and returns a turn request.</summary>
    /// <remarks>Writes an error response and returns <see langword="null"/> when validation fails.</remarks>
    /// <param name="context">HTTP context.</param>
    /// <param name="settings">Authentication and input-limit settings.</param>
    /// <param name="body">Destination for the request body; owned by the caller.</param>
    /// <param name="logger">Destination for rejected-request logs.</param>
    /// <param name="aborted">Token that signals client disconnection or host shutdown.</param>
    /// <returns>A validated turn request, or <see langword="null"/> when rejected.</returns>
    public static async Task<TurnRequest?> ReadAsync(
        HttpContext context,
        Atoms3ROptions settings,
        MemoryStream body,
        ILogger logger,
        CancellationToken aborted)
    {
        // Return authentication failures as ordinary HTTP responses before SSE begins.
        if (!string.IsNullOrEmpty(settings.Token))
        {
            var token = context.Request.Headers["X-StackChan-Token"].ToString();
            if (!FixedTimeEquals(token, settings.Token))
            {
                // Record only whether a token was presented, never its value.
                logger.LogWarning(
                    "turn stage={Stage} device={Device} presented={Presented}",
                    "rejected",
                    SafeDevice(context),
                    token.Length > 0);

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { error = "authentication failed" }, aborted);
                return null;
            }
        }

        // A device identifier is required because it becomes the session key.
        var device = context.Request.Headers["X-StackChan-Device"].ToString();
        if (string.IsNullOrWhiteSpace(device))
        {
            await RejectAsync(
                context, logger, StatusCodes.Status400BadRequest,
                "device header is required", aborted);

            return null;
        }

        var boot = context.Request.Headers["X-StackChan-Boot"].ToString();
        var conversation = context.Request.Headers["X-StackChan-Conversation"].ToString();

        var malformed =
            !DeviceHeaders.IsWellFormed(device) ? "device"
            : !DeviceHeaders.IsWellFormedOrEmpty(boot) ? "boot"
            : !DeviceHeaders.IsWellFormedOrEmpty(conversation) ? "conversation"
            : null;

        if (malformed is not null)
        {
            await RejectAsync(
                context, logger, StatusCodes.Status400BadRequest,
                $"{malformed} header is malformed", aborted);

            return null;
        }

        // Apply the body limit before reading when Content-Length is available.
        if (context.Request.ContentLength is > 0 &&
            context.Request.ContentLength > settings.MaxRequestBodyBytes)
        {
            await RejectAsync(
                context, logger, StatusCodes.Status413PayloadTooLarge,
                "body is too large", aborted);

            return null;
        }

        // Count bytes while reading so the limit also covers chunking and incorrect Content-Length values.
        if (!await TryFillBodyAsync(
                context.Request.Body, body, settings.MaxRequestBodyBytes, aborted))
        {
            await RejectAsync(
                context, logger, StatusCodes.Status413PayloadTooLarge,
                "body is too large", aborted);

            return null;
        }

        body.Position = 0;

        // JSON text input skips recognition, then follows the same path as audio input.
        var isJson = context.Request.ContentType?.Contains(
            "application/json", StringComparison.OrdinalIgnoreCase) == true;

        var sessionId = new SessionId(device);
        var turnContext = new DeviceTurnContext(new DeviceId(device), boot, conversation);

        if (isJson)
        {
            var spokenText = ReadTextField(body.GetBuffer().AsMemory(0, (int)body.Length));
            if (string.IsNullOrWhiteSpace(spokenText))
            {
                await RejectAsync(
                    context, logger, StatusCodes.Status400BadRequest,
                    "text is required", aborted);

                return null;
            }

            // Limit text separately from the body to bound agent input and conversation history.
            if (Encoding.UTF8.GetByteCount(spokenText) > settings.MaxSpokenTextBytes)
            {
                await RejectAsync(
                    context, logger, StatusCodes.Status413PayloadTooLarge,
                    "text is too large", aborted);

                return null;
            }

            return TurnRequest.FromText(sessionId, turnContext, spokenText);
        }

        if (!DeviceWav.TryRead(
            body.GetBuffer().AsSpan(0, (int)body.Length), out var audio, out var wavError))
        {
            await RejectAsync(
                context, logger, StatusCodes.Status400BadRequest,
                wavError ?? "wav is required", aborted);

            return null;
        }

        return TurnRequest.FromAudio(sessionId, turnContext, audio);
    }

    /// <summary>Returns a device identifier that is safe to include in logs.</summary>
    /// <remarks>
    /// For malformed values, this returns only the character count. This prevents unauthenticated
    /// requests from writing control characters or oversized values to logs.
    /// </remarks>
    private static string SafeDevice(HttpContext context)
    {
        var device = context.Request.Headers["X-StackChan-Device"].ToString();

        return device.Length == 0 || DeviceHeaders.IsWellFormed(device)
            ? device
            : $"<malformed:{device.Length}>";
    }

    private static async Task RejectAsync(
        HttpContext context,
        ILogger logger,
        int statusCode,
        string reason,
        CancellationToken aborted)
    {
        logger.LogWarning(
            "turn stage={Stage} device={Device} status={Status} reason={Reason}",
            "rejected",
            SafeDevice(context),
            statusCode,
            reason);

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { error = reason }, aborted);
    }

    /// <summary>Copies input up to the specified limit.</summary>
    /// <param name="source">Request body stream.</param>
    /// <param name="destination">Copy destination, owned by the caller.</param>
    /// <param name="maxBytes">Maximum accepted byte count.</param>
    /// <param name="aborted">Token that cancels reading.</param>
    /// <remarks>
    /// Stops reading and returns <see langword="false"/> as soon as the limit is exceeded.
    /// </remarks>
    private static async Task<bool> TryFillBodyAsync(
        Stream source, MemoryStream destination, long maxBytes, CancellationToken aborted)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, aborted);
                if (read == 0)
                {
                    return true;
                }

                if (destination.Length + read > maxBytes)
                {
                    return false;
                }

                destination.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Reads the <c>text</c> field from a JSON body.</summary>
    /// <remarks>
    /// Returns <see langword="null"/> for malformed JSON, malformed UTF-8, or a non-string value so
    /// the caller can return a 400 response.
    /// </remarks>
    private static string? ReadTextField(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty("text", out var value)
                ? value.GetString()
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Compares authentication tokens in content-independent time when their byte lengths match.
    /// </summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
