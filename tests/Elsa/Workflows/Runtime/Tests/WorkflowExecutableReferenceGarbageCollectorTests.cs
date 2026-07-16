using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
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
    public async Task Sweep_RetainsReusableActivityTemplateUntilItsFinalExecutionRootIsRemoved()
    {
        var templates = new InMemoryExecutableActivityTemplateStore();
        var template = new ExecutableActivityTemplate(
            "template-1", "hash-template-1", Executable("unused").RootActivity,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "test/1",
            new Dictionary<string, string>(), _now);
        await templates.SaveAsync(template);
        var baseExecutable = Executable("artifact-1");
        var executable = new WorkflowExecutable(
            baseExecutable.Identity,
            baseExecutable.RootActivity,
            baseExecutable.ResumeTargets,
            baseExecutable.CreatedAt,
            new Dictionary<string, string> { ["activity.templateHash"] = template.TemplateHash });
        await _executableStore.SaveAsync(executable);
        await _sourceReferenceStore.SaveAsync(Reference("wrapper-ref", executable.Identity.ArtifactId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _sourceReferenceStore.SaveAsync(Reference("template-ref", template.TemplateId, WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1)));
        await _workflowExecutionStateStore.SaveAsync(Execution("execution-1", executable.Identity.ArtifactId, WorkflowExecutionStatus.Running));
        var collector = new WorkflowExecutableReferenceGarbageCollector(
            _executableStore,
            _sourceReferenceStore,
            templates,
            _workflowExecutionStateStore,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            TimeProvider.System,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);

        var liveSweep = await collector.SweepAsync(_now);

        Assert.Equal(0, liveSweep.DeletedReferenceCount);
        Assert.NotNull(await _executableStore.FindAsync(executable.Identity.ArtifactId));
        Assert.NotNull(await templates.FindAsync(template.TemplateId));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("wrapper-ref"));
        Assert.NotNull(await _sourceReferenceStore.FindAsync("template-ref"));

        await _workflowExecutionStateStore.SaveAsync(Execution("execution-1", executable.Identity.ArtifactId, WorkflowExecutionStatus.Completed));
        var terminalSweep = await collector.SweepAsync(_now);
        Assert.Equal(0, terminalSweep.DeletedArtifactCount);
        Assert.Equal(0, terminalSweep.DeletedActivityTemplateCount);

        Assert.True(await _workflowExecutionStateStore.DeleteAsync("execution-1"));
        var collectedSweep = await collector.SweepAsync(_now);

        Assert.Equal(2, collectedSweep.DeletedReferenceCount);
        Assert.Equal(1, collectedSweep.DeletedArtifactCount);
        Assert.Equal(1, collectedSweep.DeletedActivityTemplateCount);
        Assert.Null(await _sourceReferenceStore.FindAsync("wrapper-ref"));
        Assert.Null(await _sourceReferenceStore.FindAsync("template-ref"));
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
            PinnedExecutable: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
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

    private WorkflowExecutable Executable(string artifactId) =>
        new(
            identity: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "authored-root",
                activityType: "test/activity",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor(
                    "test",
                    RuntimeActivityDescriptor.InitialSchemaVersion,
                    JsonSerializer.SerializeToElement(new { type = "test" })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
}
