using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Notifications.Strategies;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Mediator.Notifications.Channels;

/// <summary>
/// Reads queued notification contexts off the <see cref="INotificationsChannel"/> and
/// dispatches each via the <see cref="SequentialProcessingStrategy"/>. Single-reader,
/// FIFO order — enqueue order is preserved at dispatch.
/// </summary>
/// <remarks>
/// Handler exceptions are caught + logged + swallowed; the loop continues. This matches
/// the "subscriber MUST NOT break publisher" rule and ensures a flaky handler can't stall
/// the queue.
/// </remarks>
public sealed class BackgroundEventPublisher(
    INotificationsChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundEventPublisher> logger
)
    : IBackgroundTask
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var context in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await DispatchOneAsync(context, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown; expected.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task DispatchOneAsync(INotificationContext queuedContext, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();

            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                queuedContext.CancellationToken
            );

            await sender.Send(
                queuedContext.Notification,
                NotificationStrategy.Sequential,
                linkedTokenSource.Token
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown propagated during dispatch; expected.
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Background notification dispatch failed for {NotificationType}",
                queuedContext.Notification.GetType().Name
            );
        }
    }
}
