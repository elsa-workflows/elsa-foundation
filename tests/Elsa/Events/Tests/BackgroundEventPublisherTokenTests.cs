using Xunit;
using Elsa.Events.Channels;
using Elsa.Events.Core.Contracts;
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
    private static (BackgroundEventPublisher publisher, EventChannel channel, CountingInlineEventPublisher counting) Build()
    {
        var counting = new CountingInlineEventPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<IInlineEventPublisher>(counting);
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
        var context = TestEvents.Background(deadScope.Token);
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
        await channel.Writer.WriteAsync(TestEvents.Background(deadScope.Token));

        using var host = new CancellationTokenSource();
        var run = publisher.ExecuteAsync(host.Token);
        await publisher.StopAsync(CancellationToken.None);
        await run;

        Assert.Equal(host.Token, counting.LastToken);
    }
}
