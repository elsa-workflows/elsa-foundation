using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Default <see cref="IWorkflowArtifactClosureFactory"/>: assembles the portable export unit out of the three
/// stores a publish-capable engine already owns — the content-addressed executable store, the source-reference
/// store, and the trigger-binding index.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the walk is written here rather than delegated to <c>WorkflowExecutableDependencyGraph</c>.</b> That
/// service resolves the same graph and enforces the same invariants, but it fails on the <em>first</em> unresolved
/// edge and reports it only as message text. Export's caller has to render "the following artifacts are missing",
/// so this walk visits the whole graph, collects every unresolved edge, and hands them back as structured data.
/// It also reads the store edge-by-edge through <c>FindAsync</c> instead of materialising a full-store snapshot,
/// which is what <c>LoadClosureAsync</c> does and what an export of one workflow should not need.
/// </para>
/// <para>
/// <b>Determinism.</b> Every collection the envelope carries is ordinally sorted before it is returned, and the
/// dependency walk visits edges in ordinal order. Two exports of the same store state therefore produce
/// byte-identical envelopes, which is what makes an exported file diffable and a round-trip test meaningful.
/// </para>
/// <para>
/// <b>Retired Published references are still exportable.</b> A caller naming an explicit version id is asking for
/// that version, not for whatever is currently live, and a Published reference does not expire — retirement only
/// records that a later publication superseded it. Root selection therefore prefers a live reference when several
/// exist but never refuses on retirement alone, which keeps the failure taxonomy to the three the contract names
/// instead of inventing a fourth.
/// </para>
/// </remarks>
public sealed class WorkflowArtifactClosureFactory(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceReader sourceReferenceReader,
    IWorkflowTriggerBindingStore triggerBindingStore) : IWorkflowArtifactClosureFactory
{
    public async Task<WorkflowArtifactClosure> CreateAsync(string definitionVersionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId);

        var allReferences = await ReadAsync(
            definitionVersionId,
            "read the executable source references",
            async () => await sourceReferenceReader.ListAllAsync(cancellationToken: cancellationToken));

        var rootReference = SelectRootReference(definitionVersionId, allReferences);

        var root = await ReadAsync(
            definitionVersionId,
            $"read executable artifact '{rootReference.ArtifactId}'",
            async () => await executableStore.FindAsync(rootReference.ArtifactId, cancellationToken));

        if (root is null)
        {
            throw new WorkflowArtifactClosureSourceNotFoundException(
                definitionVersionId,
                $"its Published source reference '{rootReference.SourceReferenceId}' points at artifact " +
                $"'{rootReference.ArtifactId}', which the executable store no longer holds.");
        }

        var artifacts = await ResolveClosureAsync(definitionVersionId, root, cancellationToken);
        var memberIds = artifacts.Select(artifact => artifact.Identity.ArtifactId).ToHashSet(StringComparer.Ordinal);

        return new WorkflowArtifactClosure(
            WorkflowArtifactClosureFormat.CurrentVersion,
            root.Identity.ArtifactId,
            artifacts,
            await ReadCarriedReferencesAsync(definitionVersionId, memberIds, cancellationToken),
            await ReadCarriedBindingsAsync(definitionVersionId, memberIds, cancellationToken));
    }

    /// <summary>
    /// Resolves the definition version to the one Published reference the export is rooted at, or refuses with the
    /// exception type that says which of the two "nothing to export" situations this is.
    /// </summary>
    private static WorkflowExecutableSourceReference SelectRootReference(
        string definitionVersionId,
        IReadOnlyCollection<WorkflowExecutableSourceReference> allReferences)
    {
        var versionReferences = allReferences
            .Where(reference => StringComparer.Ordinal.Equals(reference.DefinitionVersionId, definitionVersionId))
            .ToArray();

        if (versionReferences.Length == 0)
        {
            throw new WorkflowArtifactClosureSourceNotFoundException(
                definitionVersionId,
                "no executable source reference of any scope exists for it, so there is nothing to export.");
        }

        var publishedReferences = versionReferences
            .Where(reference => reference.Scope == WorkflowExecutableReferenceScope.Published)
            .ToArray();

        if (publishedReferences.Length == 0)
        {
            throw new WorkflowArtifactClosureNotPublishedException(
                definitionVersionId,
                versionReferences.Select(reference => reference.Scope).ToArray());
        }

        // Live first, then newest, then ordinal on the reference id so a tie is still deterministic.
        return publishedReferences
            .OrderByDescending(reference => reference.DeletedAt is null)
            .ThenByDescending(reference => reference.PublishedAt ?? reference.CreatedAt)
            .ThenByDescending(reference => reference.CreatedAt)
            .ThenBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .First();
    }

    /// <summary>
    /// Depth-first walk of the pinned dependency edges, rooted at <paramref name="root"/>, resolving each child out
    /// of the executable store by its exact content-addressed identity.
    /// </summary>
    /// <remarks>
    /// A child is visited once no matter how many parents declare it, so a diamond yields one copy — the envelope's
    /// artifact list is a set. Every edge is still checked against the already-resolved member's hash, because
    /// deduplicating by id alone would let a second parent's contradictory pin pass unexamined.
    /// </remarks>
    private async Task<IReadOnlyList<WorkflowExecutable>> ResolveClosureAsync(
        string definitionVersionId,
        WorkflowExecutable root,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, WorkflowExecutable>(StringComparer.Ordinal)
        {
            [root.Identity.ArtifactId] = root,
        };
        var missing = new Dictionary<string, MissingWorkflowArtifactReference>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        await VisitAsync(root);

        if (missing.Count > 0)
        {
            throw new IncompleteWorkflowArtifactClosureException(
                definitionVersionId,
                root.Identity.ArtifactId,
                missing.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Value).ToArray());
        }

        return resolved
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Value)
            .ToArray();

        async Task VisitAsync(WorkflowExecutable artifact)
        {
            var artifactId = artifact.Identity.ArtifactId;
            active.Add(artifactId);
            path.Add(artifactId);

            try
            {
                foreach (var dependency in artifact.Dependencies
                             .OrderBy(dependency => dependency.ArtifactId, StringComparer.Ordinal)
                             .ThenBy(dependency => dependency.ArtifactHash, StringComparer.Ordinal))
                {
                    if (active.Contains(dependency.ArtifactId))
                        throw new WorkflowArtifactClosureCycleException(definitionVersionId, [.. path, dependency.ArtifactId]);

                    if (resolved.TryGetValue(dependency.ArtifactId, out var already))
                    {
                        // Already in the closure via another parent. The edge still has to agree with it.
                        if (!StringComparer.Ordinal.Equals(already.Identity.ArtifactHash, dependency.ArtifactHash))
                            RecordMissing(dependency, already.Identity.ArtifactHash, artifactId);

                        continue;
                    }

                    var child = await ReadAsync(
                        definitionVersionId,
                        $"read executable artifact '{dependency.ArtifactId}'",
                        async () => await executableStore.FindAsync(dependency.ArtifactId, cancellationToken));

                    if (child is null)
                    {
                        RecordMissing(dependency, storedArtifactHash: null, artifactId);
                        continue;
                    }

                    if (!StringComparer.Ordinal.Equals(child.Identity.ArtifactHash, dependency.ArtifactHash))
                    {
                        // The store holds this id under different content. Under content addressing that is
                        // corruption, and shipping it would produce an envelope whose declared edges lie.
                        RecordMissing(dependency, child.Identity.ArtifactHash, artifactId);
                        continue;
                    }

                    resolved.Add(dependency.ArtifactId, child);
                    await VisitAsync(child);
                }
            }
            finally
            {
                path.RemoveAt(path.Count - 1);
                active.Remove(artifactId);
            }
        }

        void RecordMissing(WorkflowExecutableDependency dependency, string? storedArtifactHash, string dependentArtifactId) =>
            missing.TryAdd(
                $"{dependency.ArtifactId}@{dependency.ArtifactHash}",
                new(dependency.ArtifactId, dependency.ArtifactHash, storedArtifactHash, dependentArtifactId));
    }

    /// <summary>
    /// The exporting engine's <c>Published</c>-scope references for every closure member (closure-envelope
    /// invariant 4).
    /// </summary>
    /// <remarks>
    /// Provenance, never rows to persist: the importer mints its own references and reads these only as a
    /// cross-check, which is why superseded-but-Published references are carried rather than filtered. They
    /// describe what this engine published, and hiding a superseded publish would make the cross-check narrower
    /// than the truth it is checking against.
    /// </remarks>
    private async Task<IReadOnlyList<WorkflowExecutableSourceReference>> ReadCarriedReferencesAsync(
        string definitionVersionId,
        IReadOnlySet<string> memberIds,
        CancellationToken cancellationToken)
    {
        var references = new List<WorkflowExecutableSourceReference>();

        // Source references are indexed by artifact id. Read only the closure members, and let the shared paging
        // helper traverse each provider page; this keeps a single-workflow export bounded by the closure size.
        foreach (var artifactId in memberIds.Order(StringComparer.Ordinal))
        {
            var artifactReferences = await ReadAsync(
                definitionVersionId,
                $"read the executable source references of artifact '{artifactId}'",
                async () => await sourceReferenceReader.ListAllByArtifactAsync(artifactId, cancellationToken));
            references.AddRange(artifactReferences.Where(reference => reference.Scope == WorkflowExecutableReferenceScope.Published));
        }

        return references
            .OrderBy(reference => reference.ArtifactId, StringComparer.Ordinal)
            .ThenBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The bindings this engine currently has indexed for the closure's members — expectations only.</summary>
    private async Task<IReadOnlyList<WorkflowTriggerBinding>> ReadCarriedBindingsAsync(
        string definitionVersionId,
        IReadOnlySet<string> memberIds,
        CancellationToken cancellationToken)
    {
        var bindings = new List<WorkflowTriggerBinding>();

        foreach (var artifactId in memberIds.Order(StringComparer.Ordinal))
        {
            var artifactBindings = await ReadAsync(
                definitionVersionId,
                $"read the trigger bindings of artifact '{artifactId}'",
                async () => await triggerBindingStore.ListAllByArtifactAsync(artifactId, cancellationToken));
            bindings.AddRange(artifactBindings);
        }

        return bindings
            .OrderBy(binding => binding.ArtifactId, StringComparer.Ordinal)
            .ThenBy(binding => binding.TriggerBindingId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Runs one store read and wraps any provider fault in the closure exception family (§2.23.5), so no raw
    /// storage exception crosses the publishing boundary and the original survives as <c>InnerException</c>.
    /// </summary>
    /// <remarks>
    /// Cancellation and the factory's own refusals pass through untouched: wrapping either would turn a caller's
    /// deliberate cancel, or a precise "this dependency is missing", into an opaque infrastructure fault.
    /// </remarks>
    private static async Task<T> ReadAsync<T>(string definitionVersionId, string operation, Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or WorkflowArtifactClosureException))
        {
            throw new WorkflowArtifactClosureStorageException(definitionVersionId, operation, exception);
        }
    }
}
