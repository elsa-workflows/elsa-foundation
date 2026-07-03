using Xunit;
using Elsa.Events.Channels;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Contexts;
using Elsa.Events.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Events.Tests;

/// <summary>
/// IN-2: the background publisher must dispatch under the host lifetime token only. A token
/// captured at enqueue time (e.g. an HTTP request scope) may already be cancelled by the time
/// the event is dequeued; linking it would abort — and then misreport as an error — a dispatch
/// that should run to completion.
/// </summary>
public class BackgroundEventPublisherTokenTests
{
    private sealed record TestEvent : IEvent;

    private sealed class CountingPublisher : IEventPublisher
    {
        public int Count;
        public CancellationToken LastToken;

        public Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private static (BackgroundEventPublisher publisher, EventChannel channel, CountingPublisher counting) Build()
    {
        var counting = new CountingPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<IEventPublisher>(counting);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var channel = new EventChannel();
        var publisher = new BackgroundEventPublisher(channel, scopeFactory, NullLogger<BackgroundEventPublisher>.Instance);
        return (publisher, channel, counting);
    }

    [Fact]
    public async Task AlreadyCancelledEnqueueTokenDoesNotBlockDispatch()
    {
        var (publisher, channel, counting) = Build();

        // Enqueue a context whose OWN token is already cancelled (simulating a dead request scope).
        using var deadScope = new CancellationTokenSource();
        await deadScope.CancelAsync();
        var context = new EventContext(new TestEvent(), EventPublishingStrategy.Background, null!, deadScope.Token);
        await channel.Writer.WriteAsync(context);

        using var host = new CancellationTokenSource();
        var run = publisher.ExecuteAsync(host.Token);

        await publisher.StopAsync(CancellationToken.None); // completes the writer → drain + clean exit
        await run;

        Assert.Equal(1, counting.Count); // dispatched despite the enqueue-time token being cancelled
        Assert.False(counting.LastToken.IsCancellationRequested); // dispatched under the (live) host token
    }

    [Fact]
    public async Task DispatchUsesHostTokenNotEnqueueToken()
    {
        var (publisher, channel, counting) = Build();

        using var deadScope = new CancellationTokenSource();
        await deadScope.CancelAsync();
        await channel.Writer.WriteAsync(new EventContext(new TestEvent(), EventPublishingStrategy.Background, null!, deadScope.Token));

        using var host = new CancellationTokenSource();
        var run = publisher.ExecuteAsync(host.Token);
        await publisher.StopAsync(CancellationToken.None);
        await run;

        Assert.Equal(host.Token, counting.LastToken);
    }
}
