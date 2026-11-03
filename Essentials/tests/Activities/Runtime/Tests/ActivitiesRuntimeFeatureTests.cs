using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Pipelines.Core.Contracts;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivitiesRuntimeFeatureTests
{
    [Fact]
    public async Task Library_activity_runtime_composes_one_canonical_event_pipeline_with_middleware()
    {
        var services = new ServiceCollection().AddWorkflowRuntime(); services.AddLogging();
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        services.AddSingleton<Recorder>(); services.AddEventHandler<ValidationWitness, Handler>();
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IEventPipeline>().Setup(builder => builder.UseMiddleware<Middleware>());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>().Publish(new ValidationWitness());
        Assert.Equal(new[] { "middleware", "handler" }, provider.GetRequiredService<Recorder>().Entries);
        new EventsFeature().ConfigureServices(services);
        Assert.Single(services.Where(item => item.ServiceType == typeof(IInlineEventPublisher)));
        Assert.Single(services.Where(item => item.ServiceType == typeof(IEventPipeline)));
    }
    private sealed record ValidationWitness : IEvent;
    private sealed class Recorder { public List<string> Entries { get; } = []; }
    private sealed class Middleware(PipelineDelegate<IEventContext> next, Recorder recorder) : IEventMiddleware {
        public ValueTask Invoke(IEventContext context) { recorder.Entries.Add("middleware"); return next(context); }
    }
    private sealed class Handler(Recorder recorder) : IEventHandler<ValidationWitness> {
        public Task Handle(ValidationWitness value, CancellationToken ct) { recorder.Entries.Add("handler"); return Task.CompletedTask; }
    }

    // Kept beside the composition test: scope validation. CShells builds shell providers without ValidateScopes,
    // so only this test catches a captive dependency in the activator chain (the resolver consumes a scoped store).
    [Fact]
    public async Task ComposesTheActivatorWithItsSecretInputResolver()
    {
        // The collaborator is a required constructor dependency, so the activator resolves only when the feature registers it.
        var services = new ServiceCollection().AddWorkflowRuntime();
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<ActivityActivator>(scope.ServiceProvider.GetRequiredService<IActivityActivator>());
        scope.ServiceProvider.GetRequiredService<ActivitySecretInputResolver>();
    }
}
