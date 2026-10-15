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
public class EventPipelineSetupTests : IAsyncLifetime
{
    private readonly ServiceProvider _provider;
    private readonly Recorder _recorder;

    public EventPipelineSetupTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddSingleton<Recorder>();
        services.AddEventHandler<SetupEvent, RecordingHandler>();
        _provider = services.BuildServiceProvider();
        _recorder = _provider.GetRequiredService<Recorder>();
        _provider.GetRequiredService<IEventPipeline>().Setup(builder => builder.UseMiddleware<RecordingMiddleware>());
    }

    [Fact]
    public async Task Custom_middleware_runs_and_the_handler_still_receives_the_event()
    {
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>().Publish(new SetupEvent());

        Assert.Equal(["middleware", "handler"], _recorder.Entries);
    }

    /// <summary>A context written against the public <see cref="IEventContext"/> alone goes through the public pipeline.</summary>
    [Fact]
    public async Task A_hand_written_context_runs_through_the_pipeline_to_the_handler()
    {
        using var scope = _provider.CreateScope();
        var context = new HandWrittenContext { Event = new SetupEvent(), ServiceProvider = scope.ServiceProvider };

        await _provider.GetRequiredService<IEventPipeline>().ExecuteAsync(context);

        Assert.Equal(["middleware", "handler"], _recorder.Entries);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private sealed record SetupEvent : IEvent;

    private sealed class HandWrittenContext : IEventContext
    {
        public required IEvent Event { get; init; }
        public required IServiceProvider ServiceProvider { get; init; }
        public CancellationToken CancellationToken { get; init; }
    }

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
