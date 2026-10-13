using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Pipelines.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Events.Tests;

/// <summary>
/// The event pipeline's extension seam: middleware added through <see cref="IEventPipeline.Setup"/> runs in front of
/// handler dispatch, and the handlers still receive the event.
/// </summary>
public class EventPipelineSetupTests
{
    [Fact]
    public async Task Custom_middleware_runs_and_the_handler_still_receives_the_event()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddSingleton<Recorder>();
        services.AddEventHandler<SetupEvent, RecordingHandler>();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventPipeline>().Setup(builder => builder.UseMiddleware<RecordingMiddleware>());

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>().Publish(new SetupEvent());

        Assert.Equal(["middleware", "handler"], provider.GetRequiredService<Recorder>().Entries);
    }

    private sealed record SetupEvent : IEvent;

    private sealed class Recorder
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class RecordingMiddleware(PipelineDelegate<IEventContext> next, Recorder recorder) : IEventMiddleware
    {
        public ValueTask Invoke(IEventContext context)
        {
            recorder.Entries.Add("middleware");
            return next(context);
        }
    }

    private sealed class RecordingHandler(Recorder recorder) : IEventHandler<SetupEvent>
    {
        public Task Handle(SetupEvent @event, CancellationToken cancellationToken)
        {
            recorder.Entries.Add("handler");
            return Task.CompletedTask;
        }
    }
}
