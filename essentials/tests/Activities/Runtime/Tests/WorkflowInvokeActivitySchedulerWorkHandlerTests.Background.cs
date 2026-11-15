using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
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
}
