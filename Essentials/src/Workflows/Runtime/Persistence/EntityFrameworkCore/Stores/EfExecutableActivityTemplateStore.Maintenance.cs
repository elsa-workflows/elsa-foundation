using System.Runtime.CompilerServices;
using System.Transactions;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfExecutableActivityTemplateStore
{
    // Reader observation only, not a root/cache/ledger. It cannot grant access or survive serialization.
    // Weak keys follow the returned immutable model's lifetime and distinguish identical recreated material.
    private static readonly ConditionalWeakTable<ExecutableActivityTemplate, MaterialObservation> ObservedMaterial = new();
    private sealed record MaterialObservation(string Scope, string Incarnation);

    public bool CanMaintainReferences(IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references)
    {
        if (executables is not EfWorkflowExecutableStore artifacts || references is not EfWorkflowExecutableSourceReferenceStore sources ||
            !ReferenceEquals(context, artifacts.Context) || !ReferenceEquals(context, sources.Context)) return false;
        try
        {
            var scope = RequireScope();
            return artifacts.AccessContext.RequireScope(admitPrivileged: true).Value == scope &&
                   sources.AccessContext.RequireScope(admitPrivileged: true).Value == scope;
        }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Atomically creates or adopts template material and its unchanged live source reference.</summary>
    /// <remarks>Returns the persisted winning reference; adoption cannot restore retirement or extend expiry.
    /// Requires a clean context without a local, enlisted or ambient transaction.</remarks>
    /// <exception cref="InvalidOperationException">The context has pending writes or a local, enlisted or ambient transaction; or no matching live reference can be established.</exception>
    /// <exception cref="RuntimeArtifactEntityFrameworkPersistenceException">Provider/IO failure or unrequested cancellation; includes template identity and original cause.</exception>
    public ValueTask<WorkflowExecutableSourceReference> SaveWithReferenceAsync(ExecutableActivityTemplate template,
        WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template); ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested(); RequireMaintenanceBoundary();
        var access = new MaintenanceAccess(AccessContext);
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, access, continuationCodec);
        return new EfActivityPublicationRuntimeCommit(WithAccess(access), sources).CommitLiveReferenceAsync(template, reference, timeProvider, cancellationToken);
    }

    internal void RequireMaintenanceBoundary()
    {
        if (context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null ||
            System.Transactions.Transaction.Current is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Template maintenance requires a clean context and its own transaction.");
    }

    /// <summary>Conditionally removes an unchanged doomed reference within this provider's native material boundary.</summary>
    /// <exception cref="InvalidOperationException">The context has pending writes or a local, enlisted or ambient transaction.</exception>
    /// <exception cref="RuntimeArtifactEntityFrameworkPersistenceException">Provider/IO failure or unrequested cancellation; includes template identity and original cause.</exception>
    public ValueTask<bool> TryDeleteDoomedReferenceAsync(WorkflowExecutableSourceReference expectedReference,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedReference.SourceReferenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedReference.ArtifactId);
        cancellationToken.ThrowIfCancellationRequested();
        return MaintainAsync(null, expectedReference, now, cancellationToken);
    }

    /// <summary>Conditionally collects material observed through this provider's actual reader.</summary>
    /// <remarks>Use the immutable object returned by Find/FindByHash/List, not a serialized/reconstructed copy.
    /// Its weak process-local scope/incarnation observation is revalidated against physical state; it grants no access.</remarks>
    /// <exception cref="InvalidOperationException">The context has pending writes or a local, enlisted or ambient transaction.</exception>
    /// <exception cref="RuntimeArtifactEntityFrameworkPersistenceException">Provider/IO failure or unrequested cancellation; includes template identity and original cause.</exception>
    public ValueTask<bool> TryDeleteUnreferencedAsync(ExecutableActivityTemplate expectedTemplate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedTemplate); cancellationToken.ThrowIfCancellationRequested();
        return MaintainAsync(expectedTemplate, null, default, cancellationToken);
    }

    private async ValueTask<bool> MaintainAsync(ExecutableActivityTemplate? expectedTemplate,
        WorkflowExecutableSourceReference? expectedReference, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Own the complete commit; do not flush or discard a caller's pending writes/transaction.
        RequireMaintenanceBoundary();
        // Unchanged tracked reads are not write authority and must not collide with fresh physical snapshots.
        context.ChangeTracker.Clear();
        var access = new MaintenanceAccess(AccessContext);
        var scope = access.Current.RequireScope(admitPrivileged: true).Value;
        var templateId = expectedTemplate?.TemplateId ?? expectedReference!.ArtifactId;
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, access, continuationCodec);
        try
        {
            // Capture the native candidate version before discovery. Every participating referencer writer
            // advances this row in its own commit. Never retry deletion using stale discovery after losing CAS.
            var row = await context.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == CreateId(scope, templateId), cancellationToken);
            if (row is null) return false;
            var identity = new TemplateIdentity(scope, templateId, row.TemplateHash);
            var material = Read(row, identity);
            var claim = await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == HashClaimId(scope, material.TemplateHash), cancellationToken)
                ?? throw new InvalidDataException("Template maintenance found material without its hash claim.");
            EnsureOwnedClaim(claim, identity); EnsureMatchingIncarnation(row, claim);
            if (expectedTemplate is not null &&
                (!ObservedMaterial.TryGetValue(expectedTemplate, out var observation) || observation.Scope != scope ||
                 observation.Incarnation != row.IncarnationId || expectedTemplate.CreatedAt != material.CreatedAt ||
                 !JsonNode.DeepEquals(ComparableContent(expectedTemplate), ComparableContent(material)))) return false;

            (WorkflowExecutableSourceReferenceEntity Row, WorkflowExecutableSourceReference Value)? source = null;
            if (expectedReference is not null)
            {
                source = await sources.ReadPhysicalMaintenanceSourceAsync(scope, expectedReference.SourceReferenceId, cancellationToken);
                if (source is null || !WorkflowExecutableSourceReferenceComparer.SameSnapshot(source.Value.Value, expectedReference) || source.Value.Value.IsLive(now)) return false;
            }
            var executables = await new EfWorkflowExecutableStore(context, access).ReadMaintenanceArtifactsAsync(scope, cancellationToken);
            var parentRows = await ReadCheckedMaintenanceRowsAsync<ExecutableActivityTemplateEntity>(context, scope, cancellationToken);
            var parents = parentRows.Select(parent => Read(parent, new TemplateIdentity(scope, DecodeMaintenanceId(parent.TemplateId), parent.TemplateHash))).ToArray();
            if (HasPhysicalReferencer(material, executables, parents)) return false;
            // Scan complete scoped reference pages so a corrupt artifact projection cannot hide a live row.
            // This is conservative finite maintenance, not a throughput claim.
            if (expectedTemplate is not null && (await sources.ReadMaintenanceSourcesAsync(scope, cancellationToken))
                    .Any(reference => reference.ArtifactId == templateId)) return false;

            cancellationToken.ThrowIfCancellationRequested();
            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context, "maintaining template", templateId, () => context.Database.BeginTransactionAsync(cancellationToken));
            if (expectedTemplate is not null)
            {
                context.Remove(row); context.Remove(claim);
            }
            else
            {
                context.Attach(row); context.Attach(claim);
                row.Revision = checked(row.Revision + 1); claim.Revision = checked(claim.Revision + 1);
                context.Entry(row).Property(candidate => candidate.Revision).IsModified = true;
                context.Entry(claim).Property(candidate => candidate.Revision).IsModified = true;
                context.Remove(source!.Value.Row);
            }
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "maintaining template", templateId, () => context.SaveChangesAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "maintaining template", templateId, () => transaction.CommitAsync(cancellationToken));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency))
        { return false; }
        catch (RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw NormalizeProviderFailure("maintaining template", templateId, exception); }
        finally { context.ChangeTracker.Clear(); }
    }

    internal static async Task<IReadOnlyCollection<TRow>> ReadCheckedMaintenanceRowsAsync<TRow>(
        RuntimeDbContext owner, string scope, CancellationToken cancellationToken) where TRow : class
    {
        var result = new List<TRow>();
        string? after = null;
        while (true)
        {
            // Either projection may locate a contradictory current-scope row. Validate both before
            // using it as deletion authority; ordinary serving filters intentionally do not do this scan.
            var query = owner.Set<TRow>().AsNoTracking().Where(row => EF.Property<string>(row, "ScopeKey") == Encode(scope) ||
                EF.Property<string>(row, "ScopeKeyHash") == Hash(scope));
            if (after is not null) query = query.Where(row => EF.Property<string>(row, "Id").CompareTo(after) > 0);
            var page = await query.OrderBy(row => EF.Property<string>(row, "Id")).Take(RuntimeStorePageRequest.MaximumLimit)
                .Select(row => new { Row = row, Id = EF.Property<string>(row, "Id"), Scope = EF.Property<string>(row, "ScopeKey"), ScopeHash = EF.Property<string>(row, "ScopeKeyHash") })
                .ToArrayAsync(cancellationToken);
            foreach (var item in page)
            {
                if (item.Scope != Encode(scope) || item.ScopeHash != Hash(scope))
                    throw new InvalidDataException("A physical maintenance row has contradictory scope projections.");
                result.Add(item.Row);
            }
            if (page.Length < RuntimeStorePageRequest.MaximumLimit) return result;
            after = page[^1].Id;
        }
    }

    internal static string DecodeMaintenanceId(string encoded)
    {
        try { return Decode(encoded); }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        { throw new InvalidDataException("A physical maintenance identity projection is corrupt.", exception); }
    }

    private static bool HasPhysicalReferencer(ExecutableActivityTemplate candidate,
        IReadOnlyCollection<WorkflowExecutable> executables, IReadOnlyCollection<ExecutableActivityTemplate> templates) =>
        executables.Any(executable => executable.Identity.ArtifactId == candidate.TemplateId || NamesTemplate(executable.CompatibilityMetadata, candidate.TemplateHash) ||
            executable.NodesById.Values.Any(node => NamesTemplate(node.Metadata, candidate.TemplateHash))) ||
        templates.Any(parent => parent.TemplateId != candidate.TemplateId &&
            (parent.ClosedTemplates.Any(dependency => dependency.TemplateId == candidate.TemplateId || dependency.TemplateHash == candidate.TemplateHash) ||
             parent.DirectDependencies.Any(dependency => dependency.TemplateId == candidate.TemplateId || dependency.TemplateHash == candidate.TemplateHash) ||
             NamesTemplate(parent.CompatibilityMetadata, candidate.TemplateHash) || parent.NodesById.Values.Any(node => NamesTemplate(node.Metadata, candidate.TemplateHash))));

    private static bool NamesTemplate(IReadOnlyDictionary<string, string> metadata, string hash) =>
        metadata.TryGetValue("activity.templateHash", out var value) && StringComparer.Ordinal.Equals(value, hash);

    private sealed class MaintenanceAccess(PersistenceAccessContext snapshot) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => snapshot;
    }
}
