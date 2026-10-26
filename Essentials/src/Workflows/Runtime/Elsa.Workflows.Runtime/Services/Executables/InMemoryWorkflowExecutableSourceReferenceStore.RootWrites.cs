using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

public sealed partial class InMemoryWorkflowExecutableSourceReferenceStore
{
    public bool CanWriteRootReferences(IWorkflowExecutableStore executables) =>
        _executableStore is not null && ReferenceEquals(executables, _executableStore);

    public ValueTask<WorkflowExecutableSourceReference> SaveWithRootWriteLeasesAsync(WorkflowExecutableSourceReference reference,
        WorkflowExecutableIdentity root, IReadOnlyList<WorkflowExecutableRootWriteLease> leases, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(leases);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceReferenceId);
        cancellationToken.ThrowIfCancellationRequested();
        if (_executableStore is null) throw new NotSupportedException("Source publication requires its actual native executable store.");
        var grants = leases.ToArray();
        foreach (var grant in grants)
        {
            ArgumentNullException.ThrowIfNull(grant); ArgumentException.ThrowIfNullOrWhiteSpace(grant.ArtifactId);
            ArgumentException.ThrowIfNullOrWhiteSpace(grant.LeaseId); ArgumentException.ThrowIfNullOrWhiteSpace(grant.ConcurrencyToken);
        }
        foreach (var value in new[] { root.ArtifactId, root.DefinitionId, root.DefinitionVersionId, root.ArtifactVersion, root.ArtifactHash,
                     reference.SourceKind, reference.SourceId, reference.DefinitionId, reference.DefinitionVersionId, reference.ArtifactVersion }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (reference.SourceVersion is not null) ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceVersion);
        if (!Enum.IsDefined(reference.Scope)) throw new ArgumentOutOfRangeException(nameof(reference));
        // Source attribution belongs to the selected publication and may differ from deduplicated
        // material's original producer. Only the retained artifact ID joins those independent identities.
        if (reference.ArtifactId != root.ArtifactId)
            throw new ArgumentException("The source reference does not retain this executable root.", nameof(reference));
        return ValueTask.FromResult(_executableStore.ExecuteUnderMaterialGate(material =>
        {
            // Artifact -> source. Validate grants only after acquiring both gates: waiting for the source
            // gate can exhaust a grant's lifetime, and no asynchronous write/effect runs under these gates.
            lock (_gate)
                return _executableStore.ExecuteUnderRootWriteLeases(grants, _clock, () =>
                {
                    var actual = material.SingleOrDefault(value => value.Identity.ArtifactId == root.ArtifactId);
                    if (actual is null || actual.Identity != root)
                        throw new InvalidOperationException("The source root's complete executable pin no longer matches.");
                    var closure = WorkflowExecutableDependencyGraph.ResolveClosure([root], material);
                    if (!closure.Select(value => value.Identity.ArtifactId).ToHashSet(StringComparer.Ordinal)
                            .SetEquals(grants.Select(grant => grant.ArtifactId)))
                        throw new ArgumentException("The source publication requires exactly its complete closure grants.", nameof(leases));
                    cancellationToken.ThrowIfCancellationRequested();
                    var now = _clock.GetUtcNow();
                    if (!reference.IsLive(now)) throw new InvalidOperationException("The proposed source reference is no longer live.");
                    var winner = _references.GetValueOrDefault(reference.SourceReferenceId);
                    if (winner is not null && (!winner.IsLive(now) || !WorkflowExecutableSourceReferenceComparer.SameIdentity(winner,
                            reference with { CreatedAt = winner.CreatedAt, PublishedAt = winner.PublishedAt })))
                        throw new InvalidOperationException("The source reference conflicts or is no longer live.");
                    if (winner is null) _references.Add(reference.SourceReferenceId, reference);
                    return winner ?? reference;
                }, cancellationToken);
        }));
    }
}
