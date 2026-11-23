using System.Reflection;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Fact]
    public async Task BackgroundPlacementWithoutCapabilityRecordsIncidentWithoutActivatingBody()
    {
        var original = NewTypedExecutable();
        var source = original.RootActivity;
        var node = new ExecutableNode(
            executableNodeId: source.ExecutableNodeId,
            authoredActivityId: source.AuthoredActivityId,
            activityType: source.ActivityType,
            activityTypeVersion: source.ActivityTypeVersion,
            descriptor: source.Descriptor,
            inputBindings: source.InputBindings,
            outputCaptures: source.OutputCaptures,
            metadata: source.Metadata,
            executionPlacement: ActivityExecutionPlacement.Background,
            activityContract: source.ActivityContract);
        var executable = new WorkflowExecutable(original.Identity, node, original.ResumeTargets, original.CreatedAt,
            original.CompatibilityMetadata, original.IncidentStrategy);
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewRunningState(executable, new Dictionary<string, ValueEnvelope>()));
        var activator = new RecordingTypedActivator();
        await using var provider = NewProvider(activator, configure: services =>
        {
            new SerializationFeature().ConfigureServices(services);
            new ActivitiesRuntimeFeature().ConfigureServices(services);
        });

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(executable.Identity));

        Assert.Equal(0, activator.ActivateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Equal("BackgroundActivityValidationFailed", state.SubStatus);
        Assert.Contains("BG-ACT-001", state.Fault!.Message, StringComparison.Ordinal);
        Assert.Single(await _incidentStateStore.ListAsync("wfexec-1"));
        Assert.All(_checkpointWriter.ListCommits(), checkpoint => Assert.Empty(checkpoint.Commit.StateChanges.BackgroundActivityWork.Admissions));
    }

    [Fact]
    public async Task Background_admission_refuses_a_pinned_variable_reader_naming_C9()
    {
        // Spec 1230 US2: a node pinned before C9 existed never reaches a worker; admission refuses it with an incident.
        const string alias = "test.background-variable-reader";
        var original = NewTypedExecutable();
        var source = original.RootActivity;
        var contract = new ActivityContract(alias, "1.0.0", "typed", source.ActivityContract!.DescriptorPayload,
            source.ActivityContract.Inputs.Values, new ActivityResultContract(new ValueTypeDescriptor("Int32"), true, ActivityValuePolicy.Default, []),
            ["Done"], new ActivityActivationRequirement("typed", alias), SideEffectProfile.External, supportsBackgroundExecution: true);
        var node = new ExecutableNode(
            executableNodeId: source.ExecutableNodeId,
            authoredActivityId: source.AuthoredActivityId,
            activityType: alias,
            activityTypeVersion: source.ActivityTypeVersion,
            descriptor: source.Descriptor,
            inputBindings: source.InputBindings,
            outputCaptures: source.OutputCaptures,
            metadata: source.Metadata,
            executionPlacement: ActivityExecutionPlacement.Background,
            activityContract: contract);
        var executable = new WorkflowExecutable(original.Identity, node, original.ResumeTargets, original.CreatedAt,
            original.CompatibilityMetadata, original.IncidentStrategy);
        await _executableStore.SaveAsync(executable);
        await _activityStateStore.SaveAsync(NewRunningState(executable, new Dictionary<string, ValueEnvelope>()));
        var activator = new RecordingTypedActivator();
        await using var provider = NewProvider(activator, configure: services =>
        {
            new SerializationFeature().ConfigureServices(services);
            new ActivitiesRuntimeFeature().ConfigureServices(services);
            new BackgroundActivitiesFeature { RunWorkers = false }.ConfigureServices(services);
            services.AddScoped(_ => DispatchProxy.Create<IBackgroundActivityWorkStore, UnusedWorkStore>());
        });
        provider.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(BackgroundVariableReader), alias);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(executable.Identity));

        Assert.Equal(0, activator.ActivateCalls);
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal(ActivityExecutionStatus.Faulted, state!.Status);
        Assert.Equal("BackgroundActivityValidationFailed", state.SubStatus);
        Assert.StartsWith("VF-BG-001 (C9): Background execution is not eligible for activity node 'node-start': the activity reads variables by name",
            state.Fault!.Message, StringComparison.Ordinal);
        Assert.Single(await _incidentStateStore.ListAsync("wfexec-1"));
        Assert.All(_checkpointWriter.ListCommits(), checkpoint => Assert.Empty(checkpoint.Commit.StateChanges.BackgroundActivityWork.Admissions));
    }

    [BackgroundExecutionSafe]
    private sealed class BackgroundVariableReader : Activity<int>, IRuntimeScopedVariableReader
    {
        protected override ValueTask<ActivityTransition<int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(1));
    }

    // Eligibility is decided before admission touches the work store.
    public class UnusedWorkStore : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"The work store must not be used: {targetMethod?.Name}.");
    }
}
