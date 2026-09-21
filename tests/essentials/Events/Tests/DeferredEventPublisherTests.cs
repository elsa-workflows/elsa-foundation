using Elsa.Events.Channels;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Events.Tests;

/// <summary>
/// Pins the contract that made the strategy footgun dangerous: <see cref="DeferredEventPublisher"/>
/// returns BEFORE its handlers run. A handler contribution is therefore NOT observable immediately
/// after <c>Publish</c> — only after the background worker drains the channel. This is exactly why
/// the draft-validation gate must depend on <see cref="IInlineEventPublisher"/> (which awaits
/// handlers) and never on deferred delivery: a gate on deferred delivery would read an empty error
/// set and pass a draft that actually has errors.
/// </summary>
public class DeferredEventPublisherTests
{
    private sealed class ContributingEvent : IEvent
    {
        public bool Handled { get; set; }
    }

    private sealed class ContributingHandler(TaskCompletionSource ran) : IEventHandler<ContributingEvent>
    {
        public Task Handle(ContributingEvent @event, CancellationToken cancellationToken)
        {
            @event.Handled = true;
            ran.SetResult();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_returns_before_the_handler_runs_and_the_effect_appears_only_after_draining()
    {
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IEventChannel, EventChannel>();
        services.AddSingleton<IEventPipeline, EventPipeline>();
        services.AddSingleton<IEventPublishingStrategy>(Strategies.EventPublishingStrategy.Sequential);
        services.AddScoped<IEventPublisher, EventPublisher>();
        services.AddScoped<IInlineEventPublisher, InlineEventPublisher>();
        services.AddScoped<IDeferredEventPublisher, DeferredEventPublisher>();
        services.AddScoped<IEventHandler<ContributingEvent>>(_ => new ContributingHandler(ran));
        var provider = services.BuildServiceProvider();

        var channel = provider.GetRequiredService<IEventChannel>();
        var deferred = provider.GetRequiredService<IDeferredEventPublisher>();
        var @event = new ContributingEvent();

        await deferred.Publish(@event);

        // The publish call has returned; the handler has NOT run yet (nothing drained the channel).
        Assert.False(@event.Handled);
        Assert.False(ran.Task.IsCompleted);

        // Drain via the background worker; now the handler runs and the effect becomes observable.
        var worker = new BackgroundEventPublisher(channel, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackgroundEventPublisher>.Instance);
        using var host = new CancellationTokenSource();
        var run = worker.ExecuteAsync(host.Token);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        await run;

        Assert.True(@event.Handled);
    }
}
