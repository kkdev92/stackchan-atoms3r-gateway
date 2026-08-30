using Kkdev92.StackChan.Gateway.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace StackChan.Gateway.App.Tests;

/// <summary>
/// Verifies prompt shutdown while a conversation is being processed.
/// </summary>
/// <remarks>
/// Without propagating shutdown to an active turn, the host waits for dependent services until the
/// turn timeout. Shutdown interrupts processing like a client disconnect and does not send
/// <c>conversation.finished</c>.
/// </remarks>
public sealed class GracefulShutdownTests
{
    [Fact(Timeout = 30_000)]
    public async Task 停止通知を受けたら_進行中のターンが_5_秒以内に終了する()
    {
        await using var factory = new GatewayFactory();
        var block = new TaskCompletionSource();
        factory.SpeechToText.Block = block;

        using var client = factory.CreateClient();
        using var request = DeviceRequest.Speech();

        // Stop while recognition is incomplete and finish before the normal turn timeout.
        var running = client.SendAsync(request, TestContext.Current.CancellationToken);

        while (factory.SpeechToText.Calls == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        // Raise the same ApplicationStopping notification as Ctrl+C or service shutdown.
        factory.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (!running.IsCompleted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        running.IsCompleted.ShouldBeTrue("停止開始から 5 秒以内にリクエストが完了しませんでした。");

        try
        {
            using var response = await running;
        }
        catch (Exception)
        {
            // Depending on the interruption path, this appears as TaskCanceledException or an HTTP error.
        }

        // Shutdown propagates to recognition and releases the dependent-service wait.
        factory.SpeechToText.ObservedCancellation.ShouldBeTrue();

        block.TrySetResult();
    }
}
