using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class ActivityInputSnapshotCheckpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Literal_inputs_do_not_load_execution_context(bool structure)
    {
        var executable = NewExecutable(structure, ("message", DurableLiteral("message", "current")));
        var activities = new CountingActivities(_activityStateStore);
        var values = new CountingValues(_durableValueStateStore);
        var workflows = new CountingWorkflows(_workflowStateStore);
        var materializer = new RuntimeActivityInputSnapshotMaterializer(activities, values, workflows);
        // No workflow/history is seeded: literals have no dependency on it, including lazy structure inputs.
        var snapshot = await materializer.MaterializeAsync(executable, executable.RootActivity,
            NewScheduledState(), _serviceProvider, Now, default);

        Assert.Equal("current", snapshot.Values["message"].InlineValue!.Value.GetString());
        Assert.Equal(0, activities.Reads);
        Assert.Equal(0, values.Reads);
        Assert.Equal(0, workflows.Reads);
    }

    [Fact]
    public async Task Secret_reference_snapshot_does_not_load_execution_context_or_resolve_secret()
    {
        var binding = new RuntimeInputBinding("message", StringType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.SecretRead, secret: new RuntimeSecretReference("credential"));
        var executable = NewExecutable(("message", binding));
        var activities = new CountingActivities(_activityStateStore);
        var values = new CountingValues(_durableValueStateStore);
        var workflows = new CountingWorkflows(_workflowStateStore);
        var materializer = new RuntimeActivityInputSnapshotMaterializer(activities, values, workflows);

        var snapshot = await materializer.MaterializeAsync(executable, executable.RootActivity,
            NewScheduledState(), _serviceProvider, Now, default);

        Assert.Equal(ValuePresence.Withheld, snapshot.Values["message"].Presence);
        Assert.Equal(0, activities.Reads);
        Assert.Equal(0, values.Reads);
        Assert.Equal(0, workflows.Reads);
    }

    [Fact]
    public async Task Workflow_request_inputs_read_current_values_without_listing_activity_history()
    {
        var binding = new RuntimeInputBinding("message", StringType, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new RuntimeWorkflowRequestReference("message"));
        var executable = NewExecutable(("message", binding));
        await SaveWorkflowStateAsync(executable.Identity);
        var activities = new CountingActivities(_activityStateStore);
        var values = new CountingValues(_durableValueStateStore);
        var materializer = new RuntimeActivityInputSnapshotMaterializer(activities, values, _workflowStateStore);
        foreach (var text in new[] { "first", "second" })
        {
            foreach (var change in RuntimeWorkflowStateSeed.BuildSeedChanges("wfexec-1",
                         new Dictionary<string, object?> { ["message"] = text }, Now))
                await _durableValueStateStore.SaveAsync(change.State);

            var snapshot = await materializer.MaterializeAsync(executable, executable.RootActivity,
                NewScheduledState(), _serviceProvider, Now, default);
            Assert.Equal(text, snapshot.Values["message"].InlineValue!.Value.GetString());
        }

        Assert.True(values.Reads > 0);
        Assert.Equal(0, activities.Reads);
    }

    [Fact]
    public async Task Custom_binding_resolver_receives_complete_context_for_literal_inputs()
    {
        var executable = NewExecutable(("message", DurableLiteral("message", "hello")));
        await SaveWorkflowStateAsync(executable.Identity);
        await _activityStateStore.SaveAsync(NewScheduledState());
        foreach (var change in RuntimeWorkflowStateSeed.BuildSeedChanges("wfexec-1",
                     new Dictionary<string, object?> { ["other"] = "current" }, Now))
            await _durableValueStateStore.SaveAsync(change.State);
        var resolver = new ContextObservingResolver();
        using var provider = new ServiceCollection().AddSingleton<IRuntimeActivityInputMaterializer>(
            new RuntimeActivityInputMaterializer(resolver)).BuildServiceProvider();
        var materializer = new RuntimeActivityInputSnapshotMaterializer(_activityStateStore, _durableValueStateStore, _workflowStateStore);

        await materializer.MaterializeAsync(executable, executable.RootActivity, NewScheduledState(), provider, Now, default);

        Assert.NotNull(resolver.Context);
        Assert.Single(resolver.Context.RuntimeView);
        Assert.Equal("current", resolver.Context.WorkflowInputEnvelopes["other"].InlineValue!.Value.GetString());
    }

    private sealed class ContextObservingResolver : IRuntimeInputBindingResolver
    {
        public RuntimeInputBindingResolutionContext? Context { get; private set; }
        public RuntimeResolvedInput Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
        {
            Context = context;
            return new RuntimeInputBindingResolver().Resolve(binding, context);
        }
    }
}
