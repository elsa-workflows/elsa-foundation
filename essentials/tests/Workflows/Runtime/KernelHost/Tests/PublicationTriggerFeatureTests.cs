using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Events;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class PublicationTriggerFeatureTests
{
    [Fact]
    public async Task Shell_prepares_and_activates_publication_bindings_without_the_legacy_runner()
    {
        const string shellName = "publication-trigger-bindings";
        const string activationId = "orders-activation";
        const string nodeId = "orders-ready";
        const string eventName = "OrdersReady";
        var executable = Executable(Node(nodeId, Types.Event, [Literal("EventName", eventName, "String")],
            metadata: new Dictionary<string, string>
            {
                [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType
            }));
        var slotId = WorkflowActivationSlotIdentity.Create(executable.Identity.DefinitionId, "default");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCShells(shells => shells
            .WithAssemblyContaining<WorkflowsPublishingFeature>()
            .WithAssemblyContaining<WorkflowDesignValidationsFeature>()
            .WithAssemblyContaining<EventsFeature>()
            .WithAssemblyContaining<PublicationTriggerProviderFeature>()
            .AddShell(shellName, shell => shell
                .WithFeature<WorkflowsPublishingFeature>()
                .WithFeature<PublicationTriggerProviderFeature>()));
        await using var host = services.BuildServiceProvider(validateScopes: true);
        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shellName);

        WorkflowTriggerBinding prepared;
        await using (var scope = shell.ServiceProvider.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            Assert.Null(provider.GetService<IRuntimeResumptionService>());
            Assert.Null(provider.GetService<IWorkflowSchedulerDrainer>());
            Assert.Null(provider.GetService<IWorkflowExecutionStateStore>());
            Assert.Null(provider.GetService<IStimulusRouter>());
            Assert.Null(provider.GetService<IBookmarkStimulusIndex>());
            var indexer = provider.GetRequiredService<IWorkflowTriggerIndexer>();
            prepared = Assert.Single(await indexer.PrepareActivationAsync(executable, activationId, slotId));
            Assert.Equal(executable.Identity.ArtifactId, prepared.ArtifactId);
            Assert.Equal(nodeId, prepared.ExecutableNodeId);
            Assert.Equal(activationId, prepared.ActivationId);
            Assert.Equal(slotId, prepared.SlotId);
            Assert.False(prepared.IsActive);
        }

        await using var activationScope = shell.ServiceProvider.CreateAsyncScope();
        var store = activationScope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>();
        var query = new WorkflowTriggerBindingPageQuery("Event", eventName);
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await store.FindActivationStateAsync(activationId));
        Assert.Empty((await store.ListByStimulusAsync(query)).Items);
        await store.ActivateAsync(activationId, replacedActivationId: null);
        Assert.Equal(WorkflowActivationProjectionState.Active, await store.FindActivationStateAsync(activationId));
        var active = Assert.Single((await store.ListByStimulusAsync(query)).Items);
        Assert.Equal(prepared.TriggerBindingId, active.TriggerBindingId);
        Assert.True(active.IsActive);
    }

    [ShellFeature("PublicationTriggerProviderProbe")]
    public sealed class PublicationTriggerProviderFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddSingleton<IActivityTriggerStimulusProvider, EventProbeProvider>();
    }

    private sealed class EventProbeProvider : IActivityTriggerStimulusProvider
    {
        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            node.ActivityType == Types.Event && node.InputBindings["EventName"].LiteralValue?.GetString() is { } name
                ? ActivityTriggerStimulusResult.Recognized([new("Event", name)])
                : ActivityTriggerStimulusResult.NotRecognized;
    }
}
