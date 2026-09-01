using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The in-memory source-reference store's by-definition-version read — the half of the export producer's input
/// that used to be a whole-table scan filtered in the caller.
/// </summary>
public sealed class InMemoryWorkflowExecutableSourceReferenceStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lists_every_scope_of_one_definition_version_and_nothing_of_another()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore();
        await store.SaveAsync(Reference("ref-1", "artifact-1", "version-1", WorkflowExecutableReferenceScope.Published));
        await store.SaveAsync(Reference("ref-2", "artifact-1", "version-1", WorkflowExecutableReferenceScope.TestRun));
        await store.SaveAsync(Reference("ref-3", "artifact-2", "version-2", WorkflowExecutableReferenceScope.Published));
        await store.SaveAsync(Reference("ref-4", "artifact-3", "version-2", WorkflowExecutableReferenceScope.TestRun));

        Assert.Equal(
            new[] { "ref-1", "ref-2" },
            (await store.ListAllByDefinitionVersionAsync("version-1"))
                .Select(reference => reference.SourceReferenceId)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { "ref-3", "ref-4" },
            (await store.ListAllByDefinitionVersionAsync("version-2"))
                .Select(reference => reference.SourceReferenceId)
                .Order(StringComparer.Ordinal));
        Assert.Empty(await store.ListAllByDefinitionVersionAsync("version-absent"));
    }

    [Fact]
    public async Task Keeps_retired_references_of_the_definition_version()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore();
        await store.SaveAsync(Reference("ref-1", "artifact-1", "version-1", WorkflowExecutableReferenceScope.Published));
        Assert.True(await store.RetireAsync("ref-1", Now, "activation-replaced"));

        var reference = Assert.Single(await store.ListAllByDefinitionVersionAsync("version-1"));
        Assert.Equal("ref-1", reference.SourceReferenceId);
        Assert.Equal(Now, reference.DeletedAt);
    }

    [Fact]
    public async Task Pages_the_definition_version_in_ordinal_reference_order()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore();
        foreach (var index in Enumerable.Range(0, 5))
        {
            await store.SaveAsync(Reference($"ref-{index:D2}", "artifact-1", "version-1", WorkflowExecutableReferenceScope.Published));
            await store.SaveAsync(Reference($"other-{index:D2}", "artifact-2", "version-2", WorkflowExecutableReferenceScope.Published));
        }

        var first = await store.ListByDefinitionVersionPageAsync(
            new WorkflowExecutableSourceReferenceDefinitionVersionPageQuery("version-1", limit: 2));
        Assert.Equal(new[] { "ref-00", "ref-01" }, first.Items.Select(reference => reference.SourceReferenceId));
        Assert.NotNull(first.NextContinuationToken);

        var traversed = (await store.ListAllByDefinitionVersionAsync("version-1"))
            .Select(reference => reference.SourceReferenceId)
            .ToArray();
        Assert.Equal(new[] { "ref-00", "ref-01", "ref-02", "ref-03", "ref-04" }, traversed);
    }

    [Fact]
    public async Task Refuses_a_continuation_minted_by_a_different_query()
    {
        var store = new InMemoryWorkflowExecutableSourceReferenceStore();
        foreach (var index in Enumerable.Range(0, 3))
            await store.SaveAsync(Reference($"ref-{index:D2}", "artifact-1", "version-1", WorkflowExecutableReferenceScope.Published));

        var page = await store.ListByDefinitionVersionPageAsync(
            new WorkflowExecutableSourceReferenceDefinitionVersionPageQuery("version-1", limit: 1));
        Assert.NotNull(page.NextContinuationToken);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ListByArtifactPageAsync(
            new WorkflowExecutableSourceReferenceArtifactPageQuery("artifact-1", continuationToken: page.NextContinuationToken)));
    }

    [Fact]
    public async Task Falls_back_to_a_residual_filter_for_a_reader_without_the_route()
    {
        var backing = new InMemoryWorkflowExecutableSourceReferenceStore();
        foreach (var index in Enumerable.Range(0, 6))
            await backing.SaveAsync(Reference($"ref-{index:D2}", "artifact-1", index % 2 == 0 ? "version-1" : "version-2", WorkflowExecutableReferenceScope.Published));

        IWorkflowExecutableSourceReferenceReader unnarrowed = new UnnarrowedReader(backing);

        Assert.Equal(
            new[] { "ref-00", "ref-02", "ref-04" },
            (await unnarrowed.ListAllByDefinitionVersionAsync("version-1"))
                .Select(reference => reference.SourceReferenceId));
        Assert.Empty(await unnarrowed.ListAllByDefinitionVersionAsync("version-absent"));

        var page = await unnarrowed.ListByDefinitionVersionPageAsync(
            new WorkflowExecutableSourceReferenceDefinitionVersionPageQuery("version-1", limit: 1));
        Assert.Equal("ref-00", Assert.Single(page.Items).SourceReferenceId);
        Assert.NotNull(page.NextContinuationToken);
    }

    private sealed class UnnarrowedReader(IWorkflowExecutableSourceReferenceReader inner) : IWorkflowExecutableSourceReferenceReader
    {
        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(sourceReferenceId, cancellationToken);

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(
            WorkflowExecutableSourceReferenceArtifactPageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListByArtifactPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(
            WorkflowExecutableSourceReferencePageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListPageAsync(query, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(
            WorkflowExecutableArtifactCandidateBatch candidates,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            inner.ListUnreferencedArtifactIdsAsync(candidates, now, cancellationToken);
    }

    private static WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        string artifactId,
        string definitionVersionId,
        WorkflowExecutableReferenceScope scope) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: artifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: definitionVersionId,
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: definitionVersionId,
            ArtifactVersion: "1.0.0",
            CreatedAt: Now,
            PublishedAt: scope == WorkflowExecutableReferenceScope.Published ? Now : null,
            Scope: scope,
            ExpiresAt: scope == WorkflowExecutableReferenceScope.TestRun ? Now.AddHours(1) : null);
}
