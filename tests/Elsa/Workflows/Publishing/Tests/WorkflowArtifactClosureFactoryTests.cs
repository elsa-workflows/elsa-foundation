using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// §2.23.2 behaviour tests for the FR-B-010 export producer: what a closure contains, and every reason it refuses
/// to produce one.
/// </summary>
/// <remarks>
/// The subject is always the real <c>WorkflowArtifactClosureFactory</c> reading the real in-memory stores, over
/// artifacts whose content-addressed identities the real hasher derived. Nothing asserts against a hand-assembled
/// expected answer: the tests state a property the envelope must have (the root is present, a shared child appears
/// once, no <c>TestRun</c> reference survives) and let the production code derive the rest.
/// </remarks>
public sealed class WorkflowArtifactClosureFactoryTests
{
    [Fact]
    public async Task Walks_the_dependency_closure_transitively_from_parent_to_grandchild()
    {
        var fixture = new WorkflowArtifactExportFixture();

        // grandchild <- child <- parent, each published in its own right as a dispatchable workflow.
        var grandchild = WorkflowArtifactExportFixture.Executable("grandchild", WorkflowArtifactExportFixture.Node("gc-root"));
        await fixture.PublishAsync(grandchild);

        var child = WorkflowArtifactExportFixture.Executable(
            "child",
            WorkflowArtifactExportFixture.Node("c-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(grandchild, "c-root"));
        await fixture.PublishAsync(child);

        var parent = WorkflowArtifactExportFixture.Executable(
            "parent",
            WorkflowArtifactExportFixture.Node("p-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(child, "p-root"));
        await fixture.PublishAsync(parent);

        var closure = await fixture.CreateFactory().CreateAsync(parent.Identity.DefinitionVersionId);

        Assert.Equal(WorkflowArtifactClosureFormat.CurrentVersion, closure.FormatVersion);
        Assert.Equal(parent.Identity.ArtifactId, closure.RootArtifactId);

        var artifactIds = closure.Artifacts.Select(artifact => artifact.Identity.ArtifactId).ToArray();
        Assert.Contains(parent.Identity.ArtifactId, artifactIds);
        Assert.Contains(child.Identity.ArtifactId, artifactIds);

        // The grandchild is the assertion that matters: it is reachable only through the child, so a one-level
        // walk would produce an envelope that looks complete and imports into a workflow that dispatches nothing.
        Assert.Contains(grandchild.Identity.ArtifactId, artifactIds);
        Assert.Equal(3, artifactIds.Length);
    }

    [Fact]
    public async Task Emits_a_child_shared_by_two_parents_exactly_once()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var shared = WorkflowArtifactExportFixture.Executable("shared", WorkflowArtifactExportFixture.Node("s-root"));
        await fixture.PublishAsync(shared);

        var left = WorkflowArtifactExportFixture.Executable(
            "left",
            WorkflowArtifactExportFixture.Node("l-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(shared, "l-root"));
        await fixture.PublishAsync(left);

        var right = WorkflowArtifactExportFixture.Executable(
            "right",
            WorkflowArtifactExportFixture.Node("r-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(shared, "r-root"));
        await fixture.PublishAsync(right);

        var root = WorkflowArtifactExportFixture.Executable(
            "root",
            WorkflowArtifactExportFixture.Node("root-node"),
            dependencies:
            [
                WorkflowArtifactExportFixture.DependencyOn(left, "root-node"),
                WorkflowArtifactExportFixture.DependencyOn(right, "root-node"),
            ]);
        await fixture.PublishAsync(root);

        var closure = await fixture.CreateFactory().CreateAsync(root.Identity.DefinitionVersionId);

        var artifactIds = closure.Artifacts.Select(artifact => artifact.Identity.ArtifactId).ToArray();

        // A set, not a multiset. The importer persists every member, and a duplicated entry would either be a
        // redundant write or a ConflictingIdentity rejection depending on which store it lands in.
        Assert.Equal(artifactIds.Length, artifactIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, artifactIds.Length);
        Assert.Single(artifactIds, id => StringComparer.Ordinal.Equals(id, shared.Identity.ArtifactId));
    }

    [Fact]
    public async Task Carries_the_published_references_and_trigger_bindings_of_every_member()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var child = WorkflowArtifactExportFixture.Executable("child", WorkflowArtifactExportFixture.Node("c-root"));
        await fixture.PublishAsync(child);
        await fixture.AddTriggerBindingAsync(child, "c-root");

        var parent = WorkflowArtifactExportFixture.Executable(
            "parent",
            WorkflowArtifactExportFixture.Node("p-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(child, "p-root"));
        await fixture.PublishAsync(parent);
        await fixture.AddTriggerBindingAsync(parent, "p-root");

        var closure = await fixture.CreateFactory().CreateAsync(parent.Identity.DefinitionVersionId);

        Assert.Equal(2, closure.SourceReferences.Count);
        Assert.All(closure.SourceReferences, reference =>
            Assert.Equal(WorkflowExecutableReferenceScope.Published, reference.Scope));

        Assert.Equal(2, closure.TriggerBindings.Count);
        Assert.Contains(closure.TriggerBindings, binding =>
            StringComparer.Ordinal.Equals(binding.ArtifactId, child.Identity.ArtifactId));
    }

    [Fact]
    public async Task Excludes_test_run_references_from_a_published_version_export()
    {
        // US3 scenario 6: a version that was test-run before it was published must not leak the draft-scoped
        // provenance of that test run into the portable envelope.
        var fixture = new WorkflowArtifactExportFixture();

        var executable = WorkflowArtifactExportFixture.Executable("wf", WorkflowArtifactExportFixture.Node("root"));
        await fixture.PublishAsync(executable);
        var testRun = await fixture.AddReferenceAsync(executable, WorkflowExecutableReferenceScope.TestRun);

        var closure = await fixture.CreateFactory().CreateAsync(executable.Identity.DefinitionVersionId);

        Assert.DoesNotContain(closure.SourceReferences, reference =>
            StringComparer.Ordinal.Equals(reference.SourceReferenceId, testRun.SourceReferenceId));
        Assert.All(closure.SourceReferences, reference =>
            Assert.Equal(WorkflowExecutableReferenceScope.Published, reference.Scope));
        Assert.DoesNotContain(closure.SourceReferences, reference => reference.ExpiresAt is not null);
    }

    [Fact]
    public async Task Refuses_a_version_whose_only_reference_is_a_test_run()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var executable = WorkflowArtifactExportFixture.Executable("draft-only", WorkflowArtifactExportFixture.Node("root"));
        await fixture.SaveArtifactAsync(executable);
        await fixture.AddReferenceAsync(
            executable,
            WorkflowExecutableReferenceScope.TestRun,
            definitionVersionId: "draft:draft-only");

        var exception = await Assert.ThrowsAsync<WorkflowArtifactClosureNotPublishedException>(
            () => fixture.CreateFactory().CreateAsync("draft:draft-only"));

        Assert.Equal("draft:draft-only", exception.DefinitionVersionId);
        Assert.Equal([WorkflowExecutableReferenceScope.TestRun], exception.ObservedScopes);
    }

    [Fact]
    public async Task Refuses_a_definition_version_it_has_never_seen()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var executable = WorkflowArtifactExportFixture.Executable("wf", WorkflowArtifactExportFixture.Node("root"));
        await fixture.PublishAsync(executable);

        // Distinct from the test-run refusal on purpose: a transport maps this one to 404 and that one to 409,
        // so collapsing them would tell an operator their unpublished draft does not exist.
        var exception = await Assert.ThrowsAsync<WorkflowArtifactClosureSourceNotFoundException>(
            () => fixture.CreateFactory().CreateAsync("wf:9.9.9"));

        Assert.Equal("wf:9.9.9", exception.DefinitionVersionId);
    }

    // Root selection is fed by the store's by-definition-version route now, not by a whole-table read the factory
    // filtered afterwards. The neighbouring version is published FIRST on purpose: references tie-break on
    // ordinal reference id, so a read that failed to narrow would hand root selection 'ref-001' — the neighbour —
    // and this test would export the wrong workflow rather than merely reading too much.
    [Fact]
    public async Task Roots_at_the_named_version_when_a_neighbouring_version_is_also_published()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var neighbour = WorkflowArtifactExportFixture.Executable(
            "wf",
            WorkflowArtifactExportFixture.Node("v2-root"),
            artifactVersion: "2.0.0");
        await fixture.PublishAsync(neighbour);

        var target = WorkflowArtifactExportFixture.Executable(
            "wf",
            WorkflowArtifactExportFixture.Node("v1-root"),
            artifactVersion: "1.0.0");
        await fixture.PublishAsync(target);

        var closure = await fixture.CreateFactory().CreateAsync(target.Identity.DefinitionVersionId);

        Assert.Equal(target.Identity.ArtifactId, closure.RootArtifactId);
        Assert.Equal(target.Identity.ArtifactId, Assert.Single(closure.Artifacts).Identity.ArtifactId);

        // The neighbour is not a closure member, so its Published reference is not provenance for this export.
        Assert.Equal(
            target.Identity.ArtifactId,
            Assert.Single(closure.SourceReferences).ArtifactId);
    }

    // The 409 has to survive a store that holds Published references for other versions of the same definition:
    // "never published" is a statement about this version, and only this version's references can settle it.
    [Fact]
    public async Task Refuses_a_test_run_only_version_even_when_a_sibling_version_is_published()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var published = WorkflowArtifactExportFixture.Executable(
            "wf",
            WorkflowArtifactExportFixture.Node("published-root"),
            artifactVersion: "1.0.0");
        await fixture.PublishAsync(published);

        var draft = WorkflowArtifactExportFixture.Executable(
            "wf",
            WorkflowArtifactExportFixture.Node("draft-root"),
            artifactVersion: "2.0.0");
        await fixture.SaveArtifactAsync(draft);
        await fixture.AddReferenceAsync(draft, WorkflowExecutableReferenceScope.TestRun);

        var exception = await Assert.ThrowsAsync<WorkflowArtifactClosureNotPublishedException>(
            () => fixture.CreateFactory().CreateAsync(draft.Identity.DefinitionVersionId));

        Assert.Equal(draft.Identity.DefinitionVersionId, exception.DefinitionVersionId);
        Assert.Equal([WorkflowExecutableReferenceScope.TestRun], exception.ObservedScopes);
    }

    [Fact]
    public async Task Refuses_when_the_published_reference_points_at_an_artifact_the_store_lost()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var executable = WorkflowArtifactExportFixture.Executable("wf", WorkflowArtifactExportFixture.Node("root"));
        await fixture.PublishAsync(executable);
        await fixture.Executables.DeleteAsync(executable.Identity.ArtifactId);

        var exception = await Assert.ThrowsAsync<WorkflowArtifactClosureSourceNotFoundException>(
            () => fixture.CreateFactory().CreateAsync(executable.Identity.DefinitionVersionId));

        Assert.Contains(executable.Identity.ArtifactId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_to_emit_a_closure_missing_a_transitive_dependency()
    {
        var fixture = new WorkflowArtifactExportFixture();

        // The grandchild is compiled but never saved: the parent's edge onto the child resolves, the child's edge
        // onto the grandchild does not. Exactly the export that would import cleanly on the machine that has the
        // grandchild already and fail on the machine that does not.
        var grandchild = WorkflowArtifactExportFixture.Executable("grandchild", WorkflowArtifactExportFixture.Node("gc-root"));

        var child = WorkflowArtifactExportFixture.Executable(
            "child",
            WorkflowArtifactExportFixture.Node("c-root"),
            dependencies: WorkflowArtifactExportFixture.DanglingDependencyOn(grandchild, "c-root"));
        await fixture.PublishAsync(child);

        var parent = WorkflowArtifactExportFixture.Executable(
            "parent",
            WorkflowArtifactExportFixture.Node("p-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(child, "p-root"));
        await fixture.PublishAsync(parent);

        var exception = await Assert.ThrowsAsync<IncompleteWorkflowArtifactClosureException>(
            () => fixture.CreateFactory().CreateAsync(parent.Identity.DefinitionVersionId));

        Assert.Equal(parent.Identity.ArtifactId, exception.RootArtifactId);
        Assert.Equal([grandchild.Identity.ArtifactId], exception.MissingArtifactIds);

        var missing = Assert.Single(exception.MissingArtifacts);
        Assert.Equal(grandchild.Identity.ArtifactHash, missing.ExpectedArtifactHash);
        Assert.Null(missing.StoredArtifactHash);
        Assert.Equal(child.Identity.ArtifactId, missing.DependentArtifactId);
    }

    [Fact]
    public async Task Reports_every_missing_dependency_rather_than_only_the_first()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var absentLeft = WorkflowArtifactExportFixture.Executable("absent-left", WorkflowArtifactExportFixture.Node("al-root"));
        var absentRight = WorkflowArtifactExportFixture.Executable("absent-right", WorkflowArtifactExportFixture.Node("ar-root"));

        var parent = WorkflowArtifactExportFixture.Executable(
            "parent",
            WorkflowArtifactExportFixture.Node("p-root"),
            dependencies:
            [
                WorkflowArtifactExportFixture.DanglingDependencyOn(absentLeft, "p-root"),
                WorkflowArtifactExportFixture.DanglingDependencyOn(absentRight, "p-root"),
            ]);
        await fixture.PublishAsync(parent);

        var exception = await Assert.ThrowsAsync<IncompleteWorkflowArtifactClosureException>(
            () => fixture.CreateFactory().CreateAsync(parent.Identity.DefinitionVersionId));

        // The endpoint renders "the following artifacts are missing", so one-at-a-time discovery would make
        // republishing a broken export an N-round-trip exercise.
        Assert.Equal(2, exception.MissingArtifactIds.Count);
        Assert.Contains(absentLeft.Identity.ArtifactId, exception.MissingArtifactIds);
        Assert.Contains(absentRight.Identity.ArtifactId, exception.MissingArtifactIds);
    }

    [Fact]
    public async Task Refuses_a_dependency_whose_stored_content_contradicts_the_pinned_hash()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var child = WorkflowArtifactExportFixture.Executable("child", WorkflowArtifactExportFixture.Node("c-root"));

        var parent = WorkflowArtifactExportFixture.Executable(
            "parent",
            WorkflowArtifactExportFixture.Node("p-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(child, "p-root"));
        await fixture.PublishAsync(parent);

        // Same id, different hash: what a store looks like after a non-content-addressed write. Under ADR 0038 an
        // id whose content differs is corruption, and shipping it would produce an envelope whose edges lie.
        await fixture.SaveCorruptedAsync(
            child.Identity with { ArtifactHash = "sha256:not-the-pinned-hash" },
            WorkflowArtifactExportFixture.Node("c-root"));

        var exception = await Assert.ThrowsAsync<IncompleteWorkflowArtifactClosureException>(
            () => fixture.CreateFactory().CreateAsync(parent.Identity.DefinitionVersionId));

        var missing = Assert.Single(exception.MissingArtifacts);
        Assert.Equal(child.Identity.ArtifactHash, missing.ExpectedArtifactHash);
        Assert.Equal("sha256:not-the-pinned-hash", missing.StoredArtifactHash);
    }

    [Fact]
    public async Task Refuses_a_cycle_in_the_stored_dependency_graph()
    {
        // A cycle is unreachable through the compiler — an artifact's hash covers its dependency edges, so the
        // child's identity must already exist before a parent that pins it can be hashed, and the back edge can
        // never be formed. It IS reachable in a store, because IWorkflowExecutableStore.SaveAsync accepts whatever
        // identity it is handed. So the walk is tested against the state it must survive: without the guard this
        // test would either not terminate or silently truncate the envelope.
        var fixture = new WorkflowArtifactExportFixture();

        var child = WorkflowArtifactExportFixture.Executable("child", WorkflowArtifactExportFixture.Node("c-root"));

        var parent = WorkflowArtifactExportFixture.Executable(
            "parent",
            WorkflowArtifactExportFixture.Node("p-root"),
            dependencies: WorkflowArtifactExportFixture.DependencyOn(child, "p-root"));
        await fixture.PublishAsync(parent);

        await fixture.SaveCorruptedAsync(
            child.Identity,
            WorkflowArtifactExportFixture.Node("c-root"),
            WorkflowArtifactExportFixture.DependencyOn(parent, "c-root"));

        var exception = await Assert.ThrowsAsync<WorkflowArtifactClosureCycleException>(
            () => fixture.CreateFactory().CreateAsync(parent.Identity.DefinitionVersionId));

        Assert.Equal(
            [parent.Identity.ArtifactId, child.Identity.ArtifactId, parent.Identity.ArtifactId],
            exception.CyclePath);
    }

    [Fact]
    public async Task Exports_a_superseded_published_version_and_roots_at_the_live_publication()
    {
        // Two Published references for one version — a republish of identical content. Both are legitimate
        // export subjects; retirement records supersession, not deletion. The live one wins root selection so the
        // choice is deterministic rather than dictionary-order.
        var fixture = new WorkflowArtifactExportFixture();

        var executable = WorkflowArtifactExportFixture.Executable("wf", WorkflowArtifactExportFixture.Node("root"));
        await fixture.PublishAsync(executable, deletedAt: new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero));
        await fixture.AddReferenceAsync(executable, WorkflowExecutableReferenceScope.Published);

        var closure = await fixture.CreateFactory().CreateAsync(executable.Identity.DefinitionVersionId);

        Assert.Equal(executable.Identity.ArtifactId, closure.RootArtifactId);
        Assert.Equal(2, closure.SourceReferences.Count);
    }

    [Fact]
    public async Task Produces_an_identical_envelope_on_every_call()
    {
        var fixture = new WorkflowArtifactExportFixture();

        var child = WorkflowArtifactExportFixture.Executable("child", WorkflowArtifactExportFixture.Node("c-root"));
        await fixture.PublishAsync(child);
        var other = WorkflowArtifactExportFixture.Executable("other", WorkflowArtifactExportFixture.Node("o-root"));
        await fixture.PublishAsync(other);

        var parent = WorkflowArtifactExportFixture.Executable(
            "parent",
            WorkflowArtifactExportFixture.Node("p-root"),
            dependencies:
            [
                WorkflowArtifactExportFixture.DependencyOn(child, "p-root"),
                WorkflowArtifactExportFixture.DependencyOn(other, "p-root"),
            ]);
        await fixture.PublishAsync(parent);
        await fixture.AddTriggerBindingAsync(parent, "p-root");
        await fixture.AddTriggerBindingAsync(child, "c-root");

        var factory = fixture.CreateFactory();
        var first = await factory.CreateAsync(parent.Identity.DefinitionVersionId);
        var second = await factory.CreateAsync(parent.Identity.DefinitionVersionId);

        // Ordering is part of the wire format's usefulness: an exported file that reshuffles between calls cannot
        // be diffed, and a round-trip test cannot assert on its bytes.
        Assert.Equal(
            first.Artifacts.Select(artifact => artifact.Identity.ArtifactId),
            second.Artifacts.Select(artifact => artifact.Identity.ArtifactId));
        Assert.Equal(
            first.SourceReferences.Select(reference => reference.SourceReferenceId),
            second.SourceReferences.Select(reference => reference.SourceReferenceId));
        Assert.Equal(
            first.TriggerBindings.Select(binding => binding.TriggerBindingId),
            second.TriggerBindings.Select(binding => binding.TriggerBindingId));
    }
}
