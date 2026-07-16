using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePublicApiCompatibilityTests
{
    [Fact]
    public void DispatchFoundation_PreservesPreexistingPublicConstructorSignatures()
    {
        AssertConstructor(
            typeof(WorkflowExecutionStartDispatchRequest),
            typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(IReadOnlyDictionary<string, string>), typeof(IReadOnlyDictionary<string, object>),
            typeof(IReadOnlyDictionary<string, object>), typeof(JsonElement?), typeof(string),
            typeof(WorkflowRunKind), typeof(WorkflowExecutableSourceSelection),
            typeof(WorkflowExecutableProvenanceRequirement));
        AssertConstructor(
            typeof(WorkflowExecutionStartCommandPayload),
            typeof(WorkflowExecutableIdentity), typeof(string),
            typeof(IReadOnlyDictionary<string, JsonElement>), typeof(IReadOnlyDictionary<string, JsonElement>),
            typeof(JsonElement?), typeof(string), typeof(WorkflowRunKind),
            typeof(WorkflowExecutableSourceProvenance));
        AssertConstructor(
            typeof(RuntimeCheckpointCommandPayload),
            typeof(WorkflowExecutableIdentity), typeof(string), typeof(IReadOnlyCollection<string>), typeof(string),
            typeof(IReadOnlyCollection<RuntimePostCommitIntent>),
            typeof(IReadOnlyDictionary<string, JsonElement>), typeof(IReadOnlyDictionary<string, JsonElement>),
            typeof(JsonElement?), typeof(string), typeof(WorkflowRunKind),
            typeof(WorkflowExecutableSourceProvenance));
        AssertConstructor(
            typeof(RuntimeCheckpointStateChangeSet),
            typeof(RuntimeStateChange<WorkflowExecutionState>), typeof(RuntimeStateChange<SchedulerState>),
            typeof(IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<BookmarkState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<DurableValueState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<IncidentState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>>),
            typeof(IReadOnlyCollection<ActivityScopeCleanupRequest>));
        AssertConstructor(
            typeof(InMemoryRuntimeCheckpointCommitStore),
            typeof(IWorkflowExecutionStateStore), typeof(IActivityExecutionStateStore), typeof(IBookmarkStateStore),
            typeof(IDurableValueStateStore), typeof(IIncidentStateStore), typeof(IExecutionLivenessStateStore),
            typeof(ISchedulerStateStore), typeof(IActivityExecutionInspectionWriter),
            typeof(IWorkflowExecutableRootWriteLeaseManager), typeof(InMemoryRuntimeCheckpointStoreState),
            typeof(TimeProvider), typeof(IActivityScopeCleanupStore),
            typeof(IActivityExecutionHierarchyWriter));
    }

    [Fact]
    public void DispatchStaging_IsAnAdditiveCapability_NotARequiredRuntimeContextMember()
    {
        var declaredNames = typeof(IRuntimeActivityExecutionContext)
            .GetMembers()
            .Where(member => member.DeclaringType == typeof(IRuntimeActivityExecutionContext))
            .Select(member => member.Name)
            .ToArray();

        Assert.DoesNotContain(nameof(IWorkflowDispatchStagingContext.StageWorkflowDispatch), declaredNames);
        Assert.DoesNotContain(nameof(IWorkflowDispatchStagingContext.WorkflowDispatchRequest), declaredNames);
    }

    private static void AssertConstructor(Type type, params Type[] parameterTypes) =>
        Assert.NotNull(type.GetConstructor(parameterTypes));
}
