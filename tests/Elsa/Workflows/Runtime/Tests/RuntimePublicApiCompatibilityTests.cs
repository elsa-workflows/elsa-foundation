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
            typeof(WorkflowExecutable),
            typeof(WorkflowExecutableIdentity), typeof(ExecutableNode),
            typeof(IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>), typeof(DateTimeOffset),
            typeof(IReadOnlyDictionary<string, string>));
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
    public void Legacy_start_and_checkpoint_constructors_default_new_authority_and_depth_members()
    {
        var identity = new WorkflowExecutableIdentity(
            "artifact-legacy",
            "definition-legacy",
            "version-legacy",
            "1.0.0",
            "sha256:legacy");
        var request = new WorkflowExecutionStartDispatchRequest("artifact-legacy", "legacy-caller");
        var command = new WorkflowExecutionStartCommandPayload(identity, "artifact-legacy");
        var checkpoint = new RuntimeCheckpointCommandPayload(identity, "Legacy", [], "Legacy");

        Assert.Null(request.StartAuthority);
        Assert.Equal(0, request.DispatchNestingDepth);
        Assert.Null(command.StartAuthority);
        Assert.Equal(0, command.DispatchNestingDepth);
        Assert.Equal(0, checkpoint.DispatchNestingDepth);
    }

    [Fact]
    public void SourceReference_PreservesPreTenantDeconstructionShape()
    {
        var reference = new WorkflowExecutableSourceReference(
            "reference",
            "artifact",
            "WorkflowDefinitionVersion",
            "version",
            "1",
            "definition",
            "version",
            "1",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            WorkflowExecutableReferenceScope.Published,
            TenantId: "tenant-a");

        var (
            sourceReferenceId,
            artifactId,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _,
            _) = reference;

        Assert.Equal("reference", sourceReferenceId);
        Assert.Equal("artifact", artifactId);
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
