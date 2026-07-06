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
///
/// Lifetime: dispatch is tied to the host/tenant lifetime token passed to <see cref="ExecuteAsync"/>.
/// The enqueue-time caller token carried on the queued context is deliberately NOT linked into
/// dispatch (IN-2): by the time a fire-and-forget event is dequeued its originating scope (e.g. an
/// HTTP request) may already be gone, and linking its since-cancelled token would abort — and then
/// misreport — a dispatch that should run to completion under host lifetime.
///
/// Graceful shutdown (IN-5): <see cref="StopAsync"/> completes the channel writer so the read loop
/// drains everything already queued and then exits cleanly, instead of dropping in-flight events.
/// It is invoked by the background-task host before the lifetime token is cancelled.
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

    /// <summary>
    /// Graceful stop: complete the channel writer so <see cref="ExecuteAsync"/>'s
    /// <see cref="System.Threading.Channels.ChannelReader{T}.ReadAllAsync"/> drains the remaining
    /// queued events and then completes normally. Bounded by construction — completing the writer
    /// forbids further enqueues, so the drain can never chase a still-filling channel and hang.
    /// Never throws (<c>TryComplete</c> is idempotent across concurrent shutdown paths).
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        channel.Writer.TryComplete();
        return Task.CompletedTask;
    }

    private async Task DispatchOneAsync(IEventContext queuedContext, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            // Drained events are delivered inline (awaited, in order) — the same sequential dispatch
            // the deferred face defers. Resolving the inline face here is intent-revealing.
            var eventPublisher = scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>();

            // Dispatch under host lifetime only. The queued context's own CancellationToken (captured
            // at enqueue time) is intentionally NOT linked here — see the class remarks (IN-2).
            await eventPublisher.Publish(
                queuedContext.Event,
                cancellationToken
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
