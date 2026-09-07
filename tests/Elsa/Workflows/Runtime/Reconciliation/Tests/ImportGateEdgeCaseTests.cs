using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// The remaining spec.md edge cases, exercised through a real reconcile pass rather than against the envelope
/// codec in isolation.
/// </summary>
/// <remarks>
/// <see cref="WorkflowArtifactClosureEnvelopeTests"/> already proves the reader and planner refuse these shapes.
/// What is asserted here is the consequence the operator actually cares about and that a unit test on the decoder
/// cannot see: the pass <em>completes</em>, the refusal arrives as a named diagnostic on the result rather than as
/// an escaped exception, and no member of the refused unit reached the executable store or an activation slot.
/// </remarks>
public sealed class ImportGateEdgeCaseTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-edge",
        Guid.NewGuid().ToString("N"));

    public ImportGateEdgeCaseTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_missing_child_dependency_rejects_the_parent()
    {
        // FR-B-010: the closure is self-contained by contract. The child is validated against the *envelope*, never
        // against the store — an envelope that only resolves because the target engine happens to already hold the
        // child would import on the machine it was tested on and fail on the one it ships to.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var absentChild = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-child"), "definition-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-parent"),
            "definition-parent",
            "1.0.0",
            ArtifactClosureFixture.DependencyOn(absentChild, "node-parent"));

        // Only the parent is carried.
        ArtifactClosureFixture.Mount(harness.Services, _mount, "orphan.json", ArtifactClosureFixture.Closure(parent));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.MissingArtifact, entry.RejectionKind);
        Assert.Contains(absentChild.Identity.ArtifactId, entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, parent.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-parent"));
    }

    [Fact]
    public async Task A_truncated_envelope_is_a_clear_diagnostic_and_no_partial_import()
    {
        // §2.23.5: the JsonException is wrapped by the reader, which owns the file path, and surfaces as a
        // diagnostic on the pass result — never as an exception escaping the reconciler.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-truncated");
        var path = ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "truncated.json",
            ArtifactClosureFixture.Closure(executable));
        var full = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, full[..(full.Length / 2)]);

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, entry.RejectionKind);
        Assert.Contains("truncated.json", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-truncated"));
    }

    [Fact]
    public async Task An_envelope_whose_format_version_this_build_does_not_know_is_rejected_loudly()
    {
        // Fail-loud, never a silent upcast: guessing at a format written by a producer this build has never seen
        // would import behaviour nobody authored, and because the store is create-only and content-addressed the
        // guess would become that id's content permanently.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-future");
        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "future.json",
            ArtifactClosureFixture.ClosureWithFormatVersion(WorkflowArtifactClosureFormat.CurrentVersion + 1, executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, entry.RejectionKind);
        Assert.Contains("format version 2 is not supported", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
    }

    [Fact]
    public async Task An_artifact_whose_payload_does_not_hash_to_its_declared_id_is_rejected_before_persistence()
    {
        // ADR 0038's content-addressing invariant: equal hash ⇔ equal behaviour. Persisting an unverified payload
        // under a claimed id would let a corrupted file *become* that id's content on a fresh engine, permanently.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var honest = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-corrupt");
        var tampered = ArtifactClosureFixture.TamperedCopy(honest, ArtifactClosureFixture.ProbeNode("node-something-else"));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "corrupt.json", ArtifactClosureFixture.Closure(tampered));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.ContentHashMismatch, entry.RejectionKind);
        Assert.Contains(honest.Identity.ArtifactId, entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, honest.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-corrupt"));
    }

    [Fact]
    public async Task An_unreadable_file_does_not_stop_later_valid_files_from_the_same_source()
    {
        // The malformed file is read first (ordinal filename order). Its failure is represented as one rejected
        // input rather than escaping the pass or terminating the iterator, so the valid sibling still imports.
        await using var harness = ArtifactImportHarness.Build(_mount);
        ArtifactClosureFixture.MountRaw(_mount, "a-broken.json", "{\"formatVersion\": 1, \"rootArtifactId\": \"artifact-");
        var valid = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-after-broken"),
            "definition-after-broken");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "b-valid.json", ArtifactClosureFixture.Closure(valid));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        var rejected = Assert.Single(result.Entries, entry => entry.Outcome == WorkflowArtifactImportOutcome.Rejected);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, rejected.RejectionKind);
        Assert.Equal(ArtifactImportHarness.SourceId, rejected.SourceId);
        Assert.Single(result.Entries, entry => entry.Outcome == WorkflowArtifactImportOutcome.Imported);
        Assert.Equal(1, result.ImportedCount);
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, valid.Identity.ArtifactId));
    }

    [Fact]
    public async Task A_batch_persistence_failure_leaves_no_member_of_the_closure_visible()
    {
        var store = new FailingAtomicBatchStore(new InMemoryWorkflowExecutableStore());
        await using var harness = ArtifactImportHarness.Build(
            _mount,
            services =>
            {
                services.RemoveAll<IWorkflowExecutableStore>();
                services.AddSingleton<IWorkflowExecutableStore>(store);
            });
        var child = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-batch-child"),
            "definition-batch-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-batch-parent"),
            "definition-batch-parent",
            dependencies: ArtifactClosureFixture.DependencyOn(child, "node-batch-parent"));
        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "atomic-batch.json",
            ArtifactClosureFixture.Closure(parent, child));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        Assert.Equal(2, result.RejectedCount);
        Assert.All(result.Entries, entry => Assert.Equal(WorkflowArtifactRejectionKind.PersistenceFailure, entry.RejectionKind));
        Assert.Equal(0, store.SequentialSaveCalls);
        Assert.Equal(1, store.BatchSaveCalls);
        Assert.Null(await store.FindAsync(parent.Identity.ArtifactId));
        Assert.Null(await store.FindAsync(child.Identity.ArtifactId));
    }

    [Fact]
    public async Task A_foreign_owner_blocks_an_older_candidate_before_version_ordering()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var incumbent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-incumbent"),
            "definition-foreign-older",
            "2.0.0");
        var candidate = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-candidate"),
            "definition-foreign-older",
            "1.0.0");

        await ArtifactImportHarness.GiveTheSlotToAsync(
            harness,
            WorkflowActivationSource.Publishing,
            "publishing-foreign-older",
            incumbent);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "foreign-older.json", ArtifactClosureFixture.Closure(candidate));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, entry.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, entry.SkipReason);
        Assert.Contains("publishing", entry.Diagnostic);
        var slot = await ArtifactImportHarness.FindSlotAsync(harness, incumbent.Identity.DefinitionId);
        Assert.NotNull(slot?.ActiveActivationId);
        var activeReference = await ArtifactImportHarness.FindReferenceAsync(harness, slot!.ActiveActivationId!);
        Assert.NotNull(activeReference);
        Assert.Equal(incumbent.Identity.ArtifactId, activeReference!.ArtifactId);
    }

    [Fact]
    public async Task A_foreign_owner_blocks_an_equal_different_artifact_before_version_ordering()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var incumbent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-incumbent"),
            "definition-foreign-equal",
            "1.0.0");
        var candidate = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-candidate"),
            "definition-foreign-equal",
            "1.0.0");

        Assert.NotEqual(incumbent.Identity.ArtifactId, candidate.Identity.ArtifactId);
        await ArtifactImportHarness.GiveTheSlotToAsync(
            harness,
            WorkflowActivationSource.Publishing,
            "publishing-foreign-equal",
            incumbent);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "foreign-equal.json", ArtifactClosureFixture.Closure(candidate));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, entry.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, entry.SkipReason);
        Assert.Contains("publishing", entry.Diagnostic);
        var slot = await ArtifactImportHarness.FindSlotAsync(harness, incumbent.Identity.DefinitionId);
        Assert.NotNull(slot?.ActiveActivationId);
        var activeReference = await ArtifactImportHarness.FindReferenceAsync(harness, slot!.ActiveActivationId!);
        Assert.NotNull(activeReference);
        Assert.Equal(incumbent.Identity.ArtifactId, activeReference!.ArtifactId);
    }

    [Fact]
    public async Task A_foreign_owner_same_artifact_remains_an_idempotent_no_op()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-same-artifact"),
            "definition-foreign-same",
            "1.0.0");

        await ArtifactImportHarness.GiveTheSlotToAsync(
            harness,
            WorkflowActivationSource.Publishing,
            "publishing-foreign-same",
            executable);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "foreign-same.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.AlreadyCurrent, entry.Outcome);
        Assert.Equal("publishing-foreign-same", entry.ActivationId);
    }

    [Fact]
    public async Task A_test_run_source_reference_is_rejected_before_persistence()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-test-run-provenance"),
            "definition-test-run-provenance");
        var closure = new WorkflowArtifactClosure(
            WorkflowArtifactClosureFormat.CurrentVersion,
            executable.Identity.ArtifactId,
            [executable],
            [SourceReference(executable, WorkflowExecutableReferenceScope.TestRun)],
            []);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "test-run-provenance.json", closure);

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, entry.RejectionKind);
        Assert.Contains("Only Published", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, executable.Identity.DefinitionId));
    }

    [Fact]
    public async Task A_published_source_reference_with_draft_definition_version_is_rejected_before_persistence()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-draft-reference"),
            "definition-draft-reference");
        var closure = new WorkflowArtifactClosure(
            WorkflowArtifactClosureFormat.CurrentVersion,
            executable.Identity.ArtifactId,
            [executable],
            [SourceReference(executable, WorkflowExecutableReferenceScope.Published, "draft:snapshot")],
            []);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "draft-reference.json", closure);

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, entry.RejectionKind);
        Assert.Contains("draft", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, executable.Identity.DefinitionId));
    }

    [Fact]
    public async Task An_artifact_with_draft_definition_version_provenance_is_rejected_before_persistence()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-draft-artifact"),
            "definition-draft-artifact");
        var draft = WithDefinitionVersionId(executable, "draft:snapshot");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "draft-artifact.json", ArtifactClosureFixture.Closure(draft));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, entry.RejectionKind);
        Assert.Contains("draft", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, executable.Identity.DefinitionId));
    }

    [Fact]
    public async Task Activation_identity_components_are_boundary_safe()
    {
        await using var harness = ArtifactImportHarness.Build(
            _mount,
            sources:
            [
                new FixedSource("a", ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "b:c")),
                new FixedSource("a:b", ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "c")),
            ]);

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        Assert.Equal(2, result.ImportedCount);
        var first = await ArtifactImportHarness.FindSlotAsync(harness, "b:c");
        var second = await ArtifactImportHarness.FindSlotAsync(harness, "c");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.ActiveActivationId, second.ActiveActivationId);
        Assert.Equal(2, (await ArtifactImportHarness.ListAllReferencesAsync(harness)).Count);
    }

    private sealed class FixedSource(string sourceId, WorkflowExecutable executable) : IWorkflowArtifactReconciliationSource
    {
        public string SourceId => sourceId;
        public string SourceKind => "test";

        public async IAsyncEnumerable<WorkflowArtifactClosureFile> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new WorkflowArtifactClosureFile(
                $"memory:{sourceId}",
                ArtifactClosureFixture.Closure(executable));
            await Task.CompletedTask;
        }
    }

    private sealed class FailingAtomicBatchStore(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner)
    {
        public int SequentialSaveCalls { get; private set; }
        public int BatchSaveCalls { get; private set; }

        public override async ValueTask SaveAsync(
            WorkflowExecutable executable,
            CancellationToken cancellationToken = default)
        {
            SequentialSaveCalls++;
            await Inner.SaveAsync(executable, cancellationToken);
            if (SequentialSaveCalls > 1)
                throw new IOException("simulated second sequential write failure");
        }

        public override ValueTask SaveBatchAsync(
            IReadOnlyList<WorkflowExecutable> executables,
            CancellationToken cancellationToken = default)
        {
            BatchSaveCalls++;
            throw new IOException("simulated atomic batch refusal");
        }
    }

    private static WorkflowExecutableSourceReference SourceReference(
        WorkflowExecutable executable,
        WorkflowExecutableReferenceScope scope,
        string? definitionVersionId = null) =>
        new(
            SourceReferenceId: $"reference-{scope}",
            ArtifactId: executable.Identity.ArtifactId,
            SourceKind: "workflow-artifact-closure",
            SourceId: "exported-artifacts",
            SourceVersion: null,
            DefinitionId: executable.Identity.DefinitionId,
            DefinitionVersionId: definitionVersionId ?? executable.Identity.DefinitionVersionId,
            ArtifactVersion: executable.Identity.ArtifactVersion,
            CreatedAt: ArtifactClosureFixture.CreatedAt,
            PublishedAt: ArtifactClosureFixture.CreatedAt,
            Scope: scope);

    private static WorkflowExecutable WithDefinitionVersionId(
        WorkflowExecutable executable,
        string definitionVersionId) =>
        new(
            identity: executable.Identity with { DefinitionVersionId = definitionVersionId },
            rootActivity: executable.RootActivity,
            resumeTargets: executable.ResumeTargets,
            createdAt: executable.CreatedAt,
            compatibilityMetadata: executable.CompatibilityMetadata,
            inputContract: executable.InputContract,
            dependencies: executable.Dependencies,
            runtimeRequirements: executable.RuntimeRequirements,
            storageDriverRequirements: executable.StorageDriverRequirements,
            incidentStrategy: executable.IncidentStrategy,
            checkpointCadence: executable.CheckpointCadence,
            workflowVariables: executable.WorkflowVariables);
}
