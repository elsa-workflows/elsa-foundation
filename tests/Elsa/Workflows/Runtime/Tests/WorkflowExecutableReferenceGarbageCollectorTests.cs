using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutableReferenceGarbageCollectorTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _sourceReferenceStore = new();
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore = new InMemoryWorkflowExecutionStateStore();

    [Fact]
    public async Task Sweep_RemovesExpiredReferencesThenTheirNowUnreferencedArtifacts_ButKeepsArtifactsWithALiveReference()
    {
        // Test (c). "kept" has a live Published reference; "doomed" has only an expired TestRun reference. One sweep
        // drops the expired reference and then prunes the artifact it left unreferenced, while "kept" survives.
        await _executableStore.SaveAsync(Executable("artifact-kept"));
        await _executableStore.SaveAsync(Executable("artifact-doomed"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-live", "artifact-kept", WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.SaveAsync(Reference("ref-expired", "artifact-doomed", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(-1)));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(1, result.DeletedArtifactCount);
        Assert.True(result.DidWork);
        Assert.NotNull(await _executableStore.FindAsync("artifact-kept"));
        Assert.Null(await _executableStore.FindAsync("artifact-doomed"));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("ref-live"));
        Assert.Null(await _sourceReferenceStore.FindAsync("ref-expired"));
    }

    [Fact]
    public async Task Sweep_PrunesArtifactWhoseOnlyReferenceWasRetired()
    {
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.RetireAsync("ref-1", _now, "deleted");

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(1, result.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync("artifact-1"));
    }

    [Fact]
    public async Task Sweep_KeepsArtifactThatStillHasAnotherLiveReference()
    {
        // Two references point at the same artifact; retiring one must not prune the artifact while the other is live.
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-live", "artifact-1", WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.SaveAsync(Reference("ref-retired", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: _now.AddMinutes(-1)));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, result.DeletedReferenceCount);
        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync("artifact-1"));
    }

    [Fact]
    public async Task Sweep_IsANoOpWhenNothingIsExpiredOrRetired()
    {
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.False(result.DidWork);
        Assert.NotNull(await _executableStore.FindAsync("artifact-1"));
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Pending)]
    [InlineData(WorkflowExecutionStatus.Running)]
    [InlineData(WorkflowExecutionStatus.Suspended)]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task Sweep_KeepsArtifactPinnedByAnyRetainedExecutionStatus(WorkflowExecutionStatus status)
    {
        var artifactId = $"artifact-{status}";
        await _executableStore.SaveAsync(Executable(artifactId));
        await _workflowExecutionStateStore.SaveAsync(Execution($"execution-{status}", artifactId, status));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync(artifactId));
    }

    [Fact]
    public async Task Sweep_CollectsArtifactOnlyAfterItsFinalRetainedExecutionRootIsRemoved()
    {
        await _executableStore.SaveAsync(Executable("artifact-1"));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-1", "artifact-1", WorkflowExecutionStatus.Completed));

        var protectedSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(0, protectedSweep.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync("artifact-1"));

        Assert.True(await _workflowExecutionStateStore.DeleteAsync("execution-1"));

        var collectibleSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, collectibleSweep.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync("artifact-1"));
    }

    [Fact]
    public async Task Sweep_ProtectsDirectAndTransitiveDependencyClosureUntilFinalRootIsRemoved()
    {
        var grandchild = Executable("artifact-grandchild");
        var child = Executable("artifact-child", grandchild);
        var parent = Executable("artifact-parent", child);
        await SaveAsync(grandchild, child, parent, Executable("artifact-orphan"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-parent", parent.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));

        var protectedSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(1, protectedSweep.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync(grandchild.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(child.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(parent.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync("artifact-orphan"));

        await _sourceReferenceStore.RetireAsync("ref-parent", _now, "final-root-removed");
        var finalSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(3, finalSweep.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync(grandchild.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync(child.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync(parent.Identity.ArtifactId));
    }

    [Fact]
    public async Task Sweep_DeduplicatesSharedDiamondClosureAndKeepsSharedChildWhileAnyRootRemains()
    {
        var leaf = Executable("artifact-leaf");
        var left = Executable("artifact-left", leaf);
        var right = Executable("artifact-right", leaf);
        var firstRoot = Executable("artifact-root-1", left, right);
        var secondRoot = Executable("artifact-root-2", right);
        await SaveAsync(leaf, left, right, firstRoot, secondRoot);
        await _sourceReferenceStore.SaveAsync(Reference("ref-root-1", firstRoot.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));
        await _sourceReferenceStore.SaveAsync(Reference("ref-root-2", secondRoot.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));

        Assert.Equal(0, (await NewGarbageCollector().SweepAsync(_now)).DeletedArtifactCount);

        await _sourceReferenceStore.RetireAsync("ref-root-1", _now, "root-removed");
        var oneRootSweep = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(2, oneRootSweep.DeletedArtifactCount);
        Assert.Null(await _executableStore.FindAsync(firstRoot.Identity.ArtifactId));
        Assert.Null(await _executableStore.FindAsync(left.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(leaf.Identity.ArtifactId));
        Assert.NotNull(await _executableStore.FindAsync(right.Identity.ArtifactId));

        await _sourceReferenceStore.RetireAsync("ref-root-2", _now, "final-root-removed");
        Assert.Equal(3, (await NewGarbageCollector().SweepAsync(_now)).DeletedArtifactCount);
    }

    [Fact]
    public async Task Sweep_FailsClosedWhenARootedDependencyIsMissingOrHasTheWrongHash()
    {
        var malformedParent = ExecutableFromEdges(
            "artifact-parent",
            new WorkflowExecutableDependency("artifact-child", "sha256:wrong", ["node-root"]));
        await SaveAsync(malformedParent, Executable("artifact-child"), Executable("artifact-orphan"));
        await _sourceReferenceStore.SaveAsync(Reference("ref-parent", malformedParent.Identity.ArtifactId, WorkflowExecutableReferenceScope.Published));

        var result = await NewGarbageCollector().SweepAsync(_now);

        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.NotNull(await _executableStore.FindAsync("artifact-orphan"));
    }

    [Fact]
    public void DependencyGraph_RejectsExactCyclesWithADeterministicFullIdentityPath()
    {
        var first = ExecutableFromEdges(
            "artifact-a",
            new WorkflowExecutableDependency("artifact-b", Hash("artifact-b"), ["node-root"]));
        var second = ExecutableFromEdges(
            "artifact-b",
            new WorkflowExecutableDependency("artifact-a", Hash("artifact-a"), ["node-root"]));

        var exception = Assert.Throws<WorkflowExecutableDependencyGraphException>(() =>
            WorkflowExecutableDependencyGraph.ResolveClosure([first.Identity], [second, first]));

        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.Cycle, exception.Kind);
        Assert.Contains("artifact-a@sha256:artifact-a -> artifact-b@sha256:artifact-b -> artifact-a@sha256:artifact-a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyGraph_RejectsAMissingArtifactWithTheExpectedFullIdentity()
    {
        var parent = ExecutableFromEdges(
            "artifact-parent",
            new WorkflowExecutableDependency("artifact-missing", "sha256:missing", ["node-root"]));

        var exception = Assert.Throws<WorkflowExecutableDependencyGraphException>(() =>
            WorkflowExecutableDependencyGraph.ResolveClosure([parent.Identity], [parent]));

        Assert.Equal(WorkflowExecutableDependencyGraphFailureKind.MissingArtifact, exception.Kind);
        Assert.Contains("artifact-missing@sha256:missing", exception.Message, StringComparison.Ordinal);
    }

    private WorkflowExecutableReferenceGarbageCollector NewGarbageCollector() =>
        new(
            _executableStore,
            _sourceReferenceStore,
            _workflowExecutionStateStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

    private WorkflowExecutionState Execution(
        string workflowExecutionId,
        string artifactId,
        WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: workflowExecutionId,
            PinnedExecutable: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            Status: status,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: status.IsTerminal() ? _now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        string artifactId,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? expiresAt = null) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: artifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: _now,
            PublishedAt: scope == WorkflowExecutableReferenceScope.Published ? _now : null,
            Scope: scope,
            ExpiresAt: expiresAt);

    private WorkflowExecutable Executable(string artifactId, params WorkflowExecutable[] dependencies) =>
        ExecutableFromEdges(
            artifactId,
            dependencies.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["node-root"])).ToArray());

    private WorkflowExecutable ExecutableFromEdges(string artifactId, params WorkflowExecutableDependency[] dependencies) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", Hash(artifactId)),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "authored-root",
                activityType: "test/activity",
                activityTypeVersion: "1.0.0",
                descriptorType: "test",
                descriptorPayload: JsonSerializer.SerializeToElement(new { type = "test" }),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: dependencies);

    private async Task SaveAsync(params WorkflowExecutable[] executables)
    {
        foreach (var executable in executables)
            await _executableStore.SaveAsync(executable);
    }

    private static string Hash(string artifactId) => $"sha256:{artifactId}";
}
