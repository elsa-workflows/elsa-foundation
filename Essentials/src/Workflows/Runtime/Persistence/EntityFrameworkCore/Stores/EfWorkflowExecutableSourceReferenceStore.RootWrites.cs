using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableSourceReferenceStore
{
    public bool CanWriteRootReferences(IWorkflowExecutableStore executables)
    {
        if (executables?.GetMaterialStore() is not EfWorkflowExecutableStore artifacts || !ReferenceEquals(context, artifacts.Context)) return false;
        try { return RequireScope() == artifacts.AccessContext.RequireScope(admitPrivileged: true).Value; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Commits one immutable live source root with native full-closure lease fencing.</summary>
    /// <exception cref="InvalidOperationException">Caller writes/transactions are pending, or full root/source identity conflicts.</exception>
    /// <exception cref="RuntimeArtifactEntityFrameworkPersistenceException">Provider/IO failure or unrequested cancellation; preserves source identity and original cause.</exception>
    public async ValueTask<WorkflowExecutableSourceReference> SaveWithRootWriteLeasesAsync(WorkflowExecutableSourceReference reference,
        WorkflowExecutableIdentity root, IReadOnlyList<WorkflowExecutableRootWriteLease> leases, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(leases);
        cancellationToken.ThrowIfCancellationRequested(); Validate(reference);
        foreach (var value in new[] { root.ArtifactId, root.DefinitionId, root.DefinitionVersionId, root.ArtifactVersion, root.ArtifactHash,
                     reference.SourceKind, reference.SourceId, reference.DefinitionId, reference.DefinitionVersionId, reference.ArtifactVersion }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Enum.IsDefined(reference.Scope)) throw new ArgumentOutOfRangeException(nameof(reference));
        // Source attribution belongs to the selected publication and may differ from deduplicated
        // material's original producer. Only the retained artifact ID joins those independent identities.
        if (reference.ArtifactId != root.ArtifactId)
            throw new ArgumentException("The source reference does not retain this executable root.", nameof(reference));
        if (context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null ||
            Transaction.Current is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Fenced source publication requires a clean context and its own transaction.");
        var grants = leases.ToArray();
        foreach (var grant in grants)
        {
            ArgumentNullException.ThrowIfNull(grant); ArgumentException.ThrowIfNullOrWhiteSpace(grant.ArtifactId);
            ArgumentException.ThrowIfNullOrWhiteSpace(grant.LeaseId); ArgumentException.ThrowIfNullOrWhiteSpace(grant.ConcurrencyToken);
        }
        if (grants.Length == 0 || grants.Select(grant => grant.ArtifactId).Distinct(StringComparer.Ordinal).Count() != grants.Length)
            throw new ArgumentException("Source publication requires distinct complete root grants.", nameof(leases));
        var capturedAccess = new RootAccess(AccessContext);
        var captured = WithAccess(capturedAccess);
        var scope = captured.ScopeForWrite(reference);
        var artifacts = new EfWorkflowExecutableStore(context, capturedAccess);
        context.ChangeTracker.Clear();
        try
        {
            // Discover every source/material fact before physical coordination/lease writes acquire locks.
            // Checked paging cannot filter a contradictory current-scope row into false absence.
            var material = await artifacts.ReadMaintenanceArtifactsAsync(scope, cancellationToken);
            var actual = material.SingleOrDefault(value => value.Identity.ArtifactId == root.ArtifactId);
            if (actual is null || actual.Identity != root)
                throw new InvalidOperationException("The source root's complete executable pin no longer matches.");
            var closure = WorkflowExecutableDependencyGraph.ResolveClosure([root], material);
            if (!closure.Select(value => value.Identity.ArtifactId).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(grants.Select(grant => grant.ArtifactId)))
                throw new ArgumentException("The source publication requires exactly its complete closure grants.", nameof(leases));
            var source = await captured.StageCreateOrAdoptAsync(reference, cancellationToken, timeProvider);
            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(context, "publishing source", reference.SourceReferenceId,
                () => context.Database.BeginTransactionAsync(cancellationToken));
            var deadlines = await artifacts.FenceRootWriteLeasesAsync(grants, timeProvider, cancellationToken);
            EnsureLive();
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(context, "publishing source", reference.SourceReferenceId,
                () => context.SaveChangesAsync(cancellationToken));
            EnsureLive();
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(context, "publishing source", reference.SourceReferenceId,
                () => transaction.CommitAsync(cancellationToken));
            return source.Reference;

            void EnsureLive()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!reference.IsLive(timeProvider.GetUtcNow()) || !source.Reference.IsLive(timeProvider.GetUtcNow()))
                    throw new InvalidOperationException("The source reference expired before publication committed.");
                foreach (var deadline in deadlines)
                    if (timeProvider.GetUtcNow() >= deadline.ExpiresAt)
                        throw new WorkflowExecutableRootWriteLeaseLostException(deadline.Grant.ArtifactId, deadline.Grant.LeaseId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw NormalizeProviderFailure("publishing source", reference.SourceReferenceId, exception); }
        finally { context.ChangeTracker.Clear(); }
    }

    private sealed class RootAccess(PersistenceAccessContext snapshot) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => snapshot;
    }
}
