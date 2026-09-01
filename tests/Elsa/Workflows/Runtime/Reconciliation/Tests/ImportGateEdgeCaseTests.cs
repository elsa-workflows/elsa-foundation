using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
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
    public async Task An_unreadable_file_stops_its_own_source_without_taking_down_the_pass()
    {
        // The malformed file is read first (ordinal filename order), so this also pins that a decode failure is
        // recorded as a rejection rather than thrown out of ReconcileAsync.
        await using var harness = ArtifactImportHarness.Build(_mount);
        ArtifactClosureFixture.MountRaw(_mount, "a-broken.json", "{\"formatVersion\": 1, \"rootArtifactId\": \"artifact-");

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(WorkflowArtifactRejectionKind.MalformedClosure, entry.RejectionKind);
        Assert.Equal(ArtifactImportHarness.SourceId, entry.SourceId);
        Assert.Equal(0, result.ImportedCount);
    }
}
