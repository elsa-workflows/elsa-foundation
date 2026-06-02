using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Events.Channels;

/// <summary>
/// Reads queued event contexts off the <see cref="IEventChannel"/> and dispatches each via
/// the <see cref="SequentialProcessingStrategy"/>. Single-reader, FIFO order — enqueue order
/// is preserved at dispatch.
/// </summary>
/// <remarks>
/// Handler exceptions are caught + logged + swallowed; the loop continues. This is where
/// fire-and-forget resilience lives: a flaky handler can't stall the queue or break the
/// publisher. The default Sequential strategy carries no such shielding by design.
/// </remarks>
public sealed class BackgroundEventPublisher(
    IEventChannel channel,
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

    private async Task DispatchOneAsync(IEventContext queuedContext, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                queuedContext.CancellationToken
            );

            await publisher.Publish(
                queuedContext.Event,
                EventPublishingStrategy.Sequential,
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
                "Background event dispatch failed for {EventType}",
                queuedContext.Event.GetType().Name
            );
        }
    }
}
