using System.Text.Json;
using Elsa.Workflows.Primitives.Models;
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
            typeof(IReadOnlyDictionary<string, string>), typeof(IncidentStrategyReference));
        // spec 117 D4 extended these convenience constructors with a trailing optional trigger-metadata channel
        // (pre-release, source-compatible for existing positional callers).
        AssertConstructor(
            typeof(WorkflowExecutionStartDispatchRequest),
            typeof(string), typeof(string), typeof(string), typeof(string),
            typeof(IReadOnlyDictionary<string, string>), typeof(IReadOnlyDictionary<string, object>),
            typeof(IReadOnlyDictionary<string, object>), typeof(JsonElement?), typeof(string),
            typeof(WorkflowRunKind), typeof(WorkflowExecutableSourceSelection),
            typeof(WorkflowExecutableProvenanceRequirement), typeof(IReadOnlyDictionary<string, string>));
        AssertConstructor(
            typeof(WorkflowExecutionStartCommandPayload),
            typeof(WorkflowExecutableIdentity), typeof(string),
            typeof(IReadOnlyDictionary<string, JsonElement>), typeof(IReadOnlyDictionary<string, JsonElement>),
            typeof(JsonElement?), typeof(string), typeof(WorkflowRunKind),
            typeof(WorkflowExecutableSourceProvenance), typeof(IReadOnlyDictionary<string, string>));
        AssertConstructor(
            typeof(RuntimeCheckpointCommandPayload),
            typeof(WorkflowExecutableIdentity), typeof(string), typeof(IReadOnlyCollection<string>), typeof(string),
            typeof(IReadOnlyCollection<RuntimePostCommitIntent>),
            typeof(IReadOnlyDictionary<string, JsonElement>), typeof(IReadOnlyDictionary<string, JsonElement>),
            typeof(JsonElement?), typeof(string), typeof(WorkflowRunKind),
            typeof(WorkflowExecutableSourceProvenance), typeof(IReadOnlyDictionary<string, string>));
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
            typeof(RuntimeCheckpointStateChangeSet),
            typeof(RuntimeStateChange<WorkflowExecutionState>), typeof(RuntimeStateChange<SchedulerState>),
            typeof(IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<BookmarkState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<DurableValueState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<IncidentState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>>),
            typeof(IReadOnlyCollection<RuntimeStateChange<WorkflowDispatchRecord>>),
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
    public async Task Dispatch_retention_preserves_legacy_delete_without_compatibility_diagnostics()
    {
        var legacyDelete = typeof(IWorkflowDispatchDeleteStore).GetMethod(
            "DeleteAsync",
            [typeof(string), typeof(CancellationToken)]);
        Assert.NotNull(legacyDelete);
        Assert.Empty(legacyDelete.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false));
        Assert.NotNull(typeof(IWorkflowDispatchDeleteStore).GetMethod(
            nameof(IWorkflowDispatchDeleteStore.TryDeleteAsync),
            [typeof(WorkflowDispatchRecord), typeof(CancellationToken)]));

        IWorkflowDispatchDeleteStore legacyStore = new LegacyWorkflowDispatchDeleteStore();
        await legacyStore.DeleteAsync("dispatch-legacy");
        Assert.Equal("dispatch-legacy", Assert.IsType<LegacyWorkflowDispatchDeleteStore>(legacyStore).DeletedDispatchId);
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

        Assert.DoesNotContain(nameof(IWorkflowDispatchStager.StageWorkflowDispatch), declaredNames);
        Assert.DoesNotContain("WorkflowDispatchRequest", declaredNames);
    }

    private static void AssertConstructor(Type type, params Type[] parameterTypes) =>
        Assert.NotNull(type.GetConstructor(parameterTypes));

    private sealed class LegacyWorkflowDispatchDeleteStore : IWorkflowDispatchDeleteStore
    {
        public string? DeletedDispatchId { get; private set; }

        public ValueTask DeleteAsync(string dispatchId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeletedDispatchId = dispatchId;
            return ValueTask.CompletedTask;
        }
    }
}
