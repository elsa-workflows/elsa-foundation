using Elsa.Tasks.Core;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public class StandaloneTriggerOutcomeRegistrationTests
{
    [Fact]
    public async Task Optional_distinct_model_owners_allow_authoring_startup_without_runtime()
    {
        foreach (var keys in new[] { Array.Empty<string>(), ["bpmn"], ["bpmn", "external-model"] })
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IInlineEventPublisher, Publisher>();
            new StandaloneTriggersApiFeature().ConfigureServices(services);
            foreach (var key in keys) services.AddSingleton<IRuntimeModelExecutionOutcomeProjector>(new Projector(key));
            await using var provider = services.BuildServiceProvider();
            foreach (var startup in provider.GetServices<IStartupTask>()) await startup.ExecuteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Duplicate_model_owners_fail_authoring_startup_with_the_model_identity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IInlineEventPublisher, Publisher>();
        new StandaloneTriggersApiFeature().ConfigureServices(services);
        services.AddSingleton<IRuntimeModelExecutionOutcomeProjector>(new Projector("external-model"));
        services.AddSingleton<IRuntimeModelExecutionOutcomeProjector>(new Projector("external-model"));
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            foreach (var startup in provider.GetServices<IStartupTask>()) await startup.ExecuteAsync(CancellationToken.None);
        });
        Assert.Contains("external-model", error.Message);
        Assert.Contains("projector", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Publisher : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Projector(string modelKey) : IRuntimeModelExecutionOutcomeProjector
    {
        public string ModelKey => modelKey;
        public WorkflowExecutionStatus? Project(RuntimeModelExecutionState execution) =>
            throw new InvalidOperationException("Startup must not inspect or execute model work.");
    }
}
