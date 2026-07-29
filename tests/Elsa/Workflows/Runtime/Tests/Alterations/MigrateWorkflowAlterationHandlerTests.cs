using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class MigrateWorkflowAlterationHandlerTests
{
    [Fact]
    public async Task PreflightAsync_StagesExactCompatibleRetainedTargetOnlyAfterQuiescence()
    {
        var source = Executable("source");
        var target = Executable("target");
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(source);
        await executables.SaveAsync(target);
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Reference(target));
        var staging = new Staging();
        var handler = Handler(executables, references, WorkflowMigrationQuiescenceReport.Quiescent());
        var workflow = Workflow(source.Identity);

        var result = await handler.PreflightAsync(Context(workflow, target.Identity, staging));

        Assert.True(result.IsAccepted);
        var change = Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(Assert.Single(staging.StagedChanges));
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(workflow);
        change.Apply(builder);
        Assert.Equal(target.Identity, builder.WorkflowExecution.PinnedExecutable);
        Assert.Equal(target.Identity.DefinitionVersionId, builder.WorkflowExecution.PinnedSource!.DefinitionVersionId);
    }

    [Fact]
    public async Task PreflightAsync_AcceptsParkedRunningWorkflowWhenAnActivityIsSuspendedAndExecutionIsQuiescent()
    {
        var source = Executable("source");
        var target = Executable("target");
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(source);
        await executables.SaveAsync(target);
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Reference(target) with { TenantId = WorkflowExecutionPartition.DefaultValue });
        var workflow = Workflow(source.Identity) with
        {
            Status = WorkflowExecutionStatus.Running,
            TenantId = null,
            Partition = new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue)
        };
        var activity = SuspendedActivity(workflow.WorkflowExecutionId);
        var inspections = new InMemoryActivityExecutionInspectionStore();
        await inspections.SaveAsync(ActivityExecutionInspectionProjection.FromState(activity, "checkpoint", DateTimeOffset.UnixEpoch));
        var staging = new Staging();

        var result = await Handler(executables, references, WorkflowMigrationQuiescenceReport.Quiescent(), inspections)
            .PreflightAsync(Context(workflow, target.Identity, staging, activity));

        Assert.True(result.IsAccepted);
        Assert.Single(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsRunningAggregateWithoutASuspendedActivity()
    {
        var source = Executable("source");
        var target = Executable("target");
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(source);
        await executables.SaveAsync(target);
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(Reference(target));
        var staging = new Staging();
        var workflow = Workflow(source.Identity) with { Status = WorkflowExecutionStatus.Running };

        var result = await Handler(executables, references, WorkflowMigrationQuiescenceReport.Quiescent())
            .PreflightAsync(Context(workflow, target.Identity, staging));

        Assert.Equal("WorkflowNotSuspended", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsMissingTargetOrForeignFenceWithoutStaging()
    {
        var source = Executable("source");
        var target = Executable("target");
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(source);
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        var missingStaging = new Staging();
        var missing = await Handler(executables, references, WorkflowMigrationQuiescenceReport.Quiescent())
            .PreflightAsync(Context(Workflow(source.Identity), target.Identity, missingStaging));

        Assert.Equal("TargetArtifactUnavailable", missing.Failure!.Code);
        Assert.Empty(missingStaging.StagedChanges);

        await executables.SaveAsync(target);
        var unretainedStaging = new Staging();
        var unretained = await Handler(executables, references, WorkflowMigrationQuiescenceReport.Quiescent())
            .PreflightAsync(Context(Workflow(source.Identity), target.Identity, unretainedStaging));

        Assert.Equal("TargetArtifactNotRetained", unretained.Failure!.Code);
        Assert.Empty(unretainedStaging.StagedChanges);

        await references.SaveAsync(Reference(target));
        var fencedStaging = new Staging();
        var fenced = await Handler(executables, references, WorkflowMigrationQuiescenceReport.Busy([new("ActiveExecutionFence")]))
            .PreflightAsync(Context(Workflow(source.Identity), target.Identity, fencedStaging));

        Assert.Equal("ActiveExecutionFence", fenced.Failure!.Code);
        Assert.Empty(fencedStaging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsWorkflowChangedAfterCapture()
    {
        var source = Executable("source");
        var target = Executable("target");
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(source);
        await executables.SaveAsync(target);
        var workflow = Workflow(source.Identity);
        var context = new WorkflowAlterationPreflightContext(
            new WorkflowAlterationJobState("job", "plan", workflow.WorkflowExecutionId, "tenant", 0, WorkflowAlterationJobStatus.Running,
                new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0,
                new WorkflowAlterationCapturedConcurrency("stale", null, source.Identity)),
            new WorkflowAlterationEnvelope("Migrate", 1, JsonSerializer.SerializeToElement(new { targetArtifact = new
            {
                artifactId = target.Identity.ArtifactId, definitionId = target.Identity.DefinitionId, definitionVersionId = target.Identity.DefinitionVersionId,
                artifactVersion = target.Identity.ArtifactVersion, artifactHash = target.Identity.ArtifactHash
            } })),
            0,
            new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([])]),
            new Staging());

        var result = await Handler(executables, new InMemoryWorkflowExecutableSourceReferenceStore(), WorkflowMigrationQuiescenceReport.Quiescent()).PreflightAsync(context);

        Assert.False(result.IsAccepted);
        Assert.Equal("WorkflowStateRevisionConflict", result.Failure!.Code);
    }

    private static MigrateWorkflowAlterationHandler Handler(
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferences,
        WorkflowMigrationQuiescenceReport quiescence,
        IActivityExecutionInspectionStore? inspectionStore = null) => new(
        executableStore,
        sourceReferences,
        new InMemoryBookmarkStateStore(),
        inspectionStore ?? new InMemoryActivityExecutionInspectionStore(),
        new FixedQuiescenceProbe(quiescence),
        ownershipContextAccessor: null,
        compatibilityValidator: new WorkflowMigrationCompatibilityValidator(),
        planner: new WorkflowMigrationPlanner(),
        timeProvider: TimeProvider.System);

    private static WorkflowAlterationPreflightContext Context(
        WorkflowExecutionState workflow,
        WorkflowExecutableIdentity target,
        Staging staging,
        params ActivityExecutionState[] activities) => new(
        new WorkflowAlterationJobState("job", "plan", workflow.WorkflowExecutionId, "tenant", 0, WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0),
        new WorkflowAlterationEnvelope("Migrate", 1, JsonSerializer.SerializeToElement(new { targetArtifact = new
        {
            artifactId = target.ArtifactId,
            definitionId = target.DefinitionId,
            definitionVersionId = target.DefinitionVersionId,
            artifactVersion = target.ArtifactVersion,
            artifactHash = target.ArtifactHash
        } })),
        0,
        new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection(activities)]), staging);

    private static WorkflowExecutionState Workflow(WorkflowExecutableIdentity identity) => new(
        "workflow", identity, WorkflowExecutionStatus.Suspended, null, DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant", new Dictionary<string, string>())
    {
        PinnedSource = new WorkflowExecutableSourceProvenance("source-ref", "published", "source", null, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion, null, null)
    };

    private static WorkflowExecutable Executable(string artifactId) => new(
        new WorkflowExecutableIdentity(artifactId, "definition", "version", "1.0.0", $"sha256:{artifactId}"),
        new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), inputContract: null, dependencies: null,
        runtimeRequirements: null, storageDriverRequirements: null, IncidentStrategyBuiltIns.FaultReference);

    private static ActivityExecutionState SuspendedActivity(string workflowExecutionId) => new(
        new ActivityExecution("activity", workflowExecutionId, "node", "node", "test", "1"),
        ActivityExecutionStatus.Suspended,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null,
        null,
        null,
        null,
        null,
        null,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>());

    private static WorkflowExecutableSourceReference Reference(WorkflowExecutable executable) => new(
        "target-ref", executable.Identity.ArtifactId, "published", "target", null, executable.Identity.DefinitionId, executable.Identity.DefinitionVersionId,
        executable.Identity.ArtifactVersion, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, WorkflowExecutableReferenceScope.Published, TenantId: "tenant");

    private sealed class FixedQuiescenceProbe(WorkflowMigrationQuiescenceReport report) : IWorkflowMigrationQuiescenceProbe
    {
        public ValueTask<WorkflowMigrationQuiescenceReport> ProbeAsync(string workflowExecutionId, RuntimeExecutionFence? currentActorFence, CancellationToken cancellationToken = default) => ValueTask.FromResult(report);
    }

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
