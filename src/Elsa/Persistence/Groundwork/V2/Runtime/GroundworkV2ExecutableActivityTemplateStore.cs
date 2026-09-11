using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Current-only Groundwork v2 executable activity templates and content-hash claims.</summary>
/// <remarks>
/// Template material and its injective hash claim are created and deleted in one exact atomic unit of
/// work. The adapter never overwrites immutable content and reconciles create races by re-reading the
/// winning rows. No v1 document-store or migration path is part of this current-only contract.
/// </remarks>
public sealed class GroundworkV2ExecutableActivityTemplateStore : GroundworkV2RuntimeStoreBase, IExecutableActivityTemplateStore
{
    private const int MaximumDeleteAttempts = 8;

    private readonly StorageUnit claimUnit;

    public GroundworkV2ExecutableActivityTemplateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
        : base(sessions, accessContextAccessor, targetName, "executable activity template", ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind)
    {
        claimUnit = UnitFor(ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind);
    }

    public async ValueTask SaveAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2ExecutableActivityTemplateStorageConventions.Validate(template);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ScopedAccess;
        RequireAtomicCommit();

        var existingById = await FindAsync(template.TemplateId, cancellationToken);
        if (existingById is not null)
        {
            EnsureSameIdentityAndContent(existingById, template);
            EnsureOwnedClaim(template, await FindClaimAsync(template.TemplateHash, cancellationToken));
            return;
        }

        var existingClaim = await FindClaimAsync(template.TemplateHash, cancellationToken);
        if (existingClaim is not null)
        {
            if (!StringComparer.Ordinal.Equals(existingClaim.TemplateId, template.TemplateId))
                throw HashCollision(template, existingClaim.TemplateId);
            throw new InvalidDataException(
                $"Executable activity template hash claim '{template.TemplateHash}' exists without its template row.");
        }

        var existingByHash = await FindByHashAsync(template.TemplateHash, cancellationToken);
        if (existingByHash is not null)
            throw HashCollision(template, existingByHash.TemplateId);

        BatchWriteReport? report;
        try
        {
            report = await TryCreateAsync(template, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Providers may surface an exact create-only race as an exception rather than a
            // materialized outcome report. The unit of work has already been rolled back; reconcile
            // the winner through the public read paths before deciding whether the save is safe.
            report = null;
        }

        if (report?.IsSuccessful == true)
            return;

        await ReconcileCreateAsync(template, report, cancellationToken);
    }

    public ValueTask<ExecutableActivityTemplate?> FindAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var physicalId = GroundworkV2ExecutableActivityTemplateStorageConventions.PhysicalId(templateId);
        cancellationToken.ThrowIfCancellationRequested();
        var entry = Open().Read(GroundworkRuntimeRowStore.Key(physicalId));
        if (entry is null)
            return ValueTask.FromResult<ExecutableActivityTemplate?>(null);

        var template = GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(template.TemplateId, templateId))
            throw new InvalidDataException(
                $"Groundwork executable activity template physical identity collision detected for '{templateId}'.");
        return ValueTask.FromResult<ExecutableActivityTemplate?>(template);
    }

    public ValueTask<ExecutableActivityTemplate?> FindByHashAsync(
        string templateHash,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId(templateHash);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(Unit.Name);
        var hash = Column(table, ElsaRuntimeV2StorageManifest.TemplateHashField);
        var templateId = Column(table, ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(hash, templateHash),
            [new OrderTerm(templateId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            Paging.Keyset(2)));
        if (result.Rows.Count > 1)
        {
            throw new InvalidOperationException(
                $"Template hash '{templateHash}' is bound to more than one stored template; the content-addressed store is corrupt.");
        }

        return ValueTask.FromResult(result.Rows.Count == 0
            ? null
            : (ExecutableActivityTemplate?)GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize(result.Rows[0]));
    }

    public ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(
        RuntimeStorePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TableId(Unit.Name);
        var collection = Column(table, ElsaRuntimeV2StorageManifest.CollectionField);
        var templateId = Column(table, ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateIdField);
        var result = Open().Query(new QueryRequest(
            table,
            Equal(collection, ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind),
            [new OrderTerm(templateId, OrderDirection.Ascending, NullOrder.Last)],
            Projection.All,
            PagingFor(request.Limit, request.ContinuationToken)));
        return ValueTask.FromResult(new RuntimeStorePage<ExecutableActivityTemplate>(
            request,
            result.Rows.Select(GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize).ToArray(),
            result.NextContinuationToken));
    }

    public async ValueTask<bool> DeleteAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        var physicalId = GroundworkV2ExecutableActivityTemplateStorageConventions.PhysicalId(templateId);
        cancellationToken.ThrowIfCancellationRequested();
        _ = ScopedAccess;
        RequireAtomicCommit();

        for (var attempt = 0; attempt < MaximumDeleteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var templateEntry = Open().Read(GroundworkRuntimeRowStore.Key(physicalId));
            if (templateEntry is null)
                return false;

            var template = GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize(templateEntry.Values.Values);
            if (!StringComparer.Ordinal.Equals(template.TemplateId, templateId))
                throw new InvalidDataException(
                    $"Groundwork executable activity template physical identity collision detected for '{templateId}'.");

            var claimEntry = OpenScoped(claimUnit).Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId(template.TemplateHash)));
            if (claimEntry is not null)
            {
                var claim = GroundworkV2ExecutableActivityTemplateStorageConventions.DeserializeClaim(claimEntry.Values.Values);
                EnsureOwnedClaim(template, claim);
            }

            using var unitOfWork = BeginAtomicUnitOfWork();
            StageDelete(unitOfWork, Unit, physicalId, templateEntry);
            if (claimEntry is not null)
            {
                StageDelete(
                    unitOfWork,
                    claimUnit,
                    GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId(template.TemplateHash),
                    claimEntry);
            }

            try
            {
                var report = await CommitAsync(unitOfWork, cancellationToken);
                if (report.IsSuccessful)
                    return true;
            }
            catch (BatchWriteException)
            {
                // Re-read on the next bounded attempt. A successor claim or a deleted template
                // must be observed before another exact delete is staged.
            }
        }

        throw new InvalidOperationException(
            $"Executable activity template '{templateId}' changed concurrently and did not settle after {MaximumDeleteAttempts} attempts.");
    }

    /// <summary>
    /// Whether this template still needs creating, for a caller about to stage it into a transaction it
    /// owns. This is the preflight <see cref="SaveAsync"/> runs before staging, kept in the lane that owns
    /// the invariants: false means an identical template is already durable and staging it again would
    /// fail a create-only write for no reason, and a template id bound to different behaviour, or a hash
    /// already claimed by another template, throws here rather than inside the caller's transaction.
    /// </summary>
    public async ValueTask<bool> RequiresCreateAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2ExecutableActivityTemplateStorageConventions.Validate(template);
        cancellationToken.ThrowIfCancellationRequested();

        var existingById = await FindAsync(template.TemplateId, cancellationToken);
        if (existingById is not null)
        {
            EnsureSameIdentityAndContent(existingById, template);
            EnsureOwnedClaim(template, await FindClaimAsync(template.TemplateHash, cancellationToken));
            return false;
        }

        var existingClaim = await FindClaimAsync(template.TemplateHash, cancellationToken);
        if (existingClaim is not null && !StringComparer.Ordinal.Equals(existingClaim.TemplateId, template.TemplateId))
            throw HashCollision(template, existingClaim.TemplateId);

        var existingByHash = await FindByHashAsync(template.TemplateHash, cancellationToken);
        if (existingByHash is not null)
            throw HashCollision(template, existingByHash.TemplateId);

        return true;
    }

    /// <summary>
    /// Stages this template's creation into a transaction the caller owns, for an operation that is one
    /// act across lanes — publishing an activity writes design rows, this template and a publication
    /// receipt, and either all of it happened or none of it did.
    /// <para>
    /// The template and its hash claim are staged create-only together, exactly as the lane's own save
    /// does, so the hash stays single-writer. The caller is responsible for the preflight this lane's
    /// <see cref="SaveAsync"/> performs first: an existing template or a foreign hash claim must be
    /// resolved before staging, because a transaction that spans lanes cannot reconcile a lost race by
    /// itself.
    /// </para>
    /// </summary>
    public static void StageCreate(GroundworkStorageTransaction transaction, ExecutableActivityTemplate template)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        GroundworkV2ExecutableActivityTemplateStorageConventions.Validate(template);
        transaction.StageInsert(
            ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
            GroundworkV2ExecutableActivityTemplateStorageConventions.Values(template),
            WriteOptions.CreateOnly);
        transaction.StageInsert(
            ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
            GroundworkV2ExecutableActivityTemplateStorageConventions.ClaimValues(template),
            WriteOptions.CreateOnly);
    }

    private async ValueTask<BatchWriteReport> TryCreateAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = BeginAtomicUnitOfWork();
        unitOfWork.Stage(RowWrite.Insert(
            Unit,
            GroundworkV2ExecutableActivityTemplateStorageConventions.Values(template),
            WriteOptions.CreateOnly));
        unitOfWork.Stage(RowWrite.Insert(
            claimUnit,
            GroundworkV2ExecutableActivityTemplateStorageConventions.ClaimValues(template),
            WriteOptions.CreateOnly));
        return await CommitAsync(unitOfWork, cancellationToken);
    }

    private async ValueTask ReconcileCreateAsync(
        ExecutableActivityTemplate template,
        BatchWriteReport? report,
        CancellationToken cancellationToken)
    {
        var winnerById = await FindAsync(template.TemplateId, cancellationToken);
        if (winnerById is not null)
        {
            EnsureSameIdentityAndContent(winnerById, template);
            EnsureOwnedClaim(template, await FindClaimAsync(template.TemplateHash, cancellationToken));
            return;
        }

        var claim = await FindClaimAsync(template.TemplateHash, cancellationToken);
        if (claim is not null)
        {
            if (!StringComparer.Ordinal.Equals(claim.TemplateId, template.TemplateId))
                throw HashCollision(template, claim.TemplateId);
            throw new InvalidDataException(
                $"Executable activity template hash claim '{template.TemplateHash}' exists without its template row.");
        }

        var winnerByHash = await FindByHashAsync(template.TemplateHash, cancellationToken);
        if (winnerByHash is not null)
            throw HashCollision(template, winnerByHash.TemplateId);

        var failure = report is null
            ? "a provider write exception"
            : $"{report.Failed} failed row outcomes";
        throw new InvalidOperationException(
            $"Groundwork rejected executable activity template creation with {failure} and no winning row could be reconciled.");
    }

    private async ValueTask<GroundworkV2ExecutableActivityTemplateStorageConventions.TemplateHashClaim?> FindClaimAsync(
        string templateHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claimId = GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId(templateHash);
        var entry = OpenScoped(claimUnit).Read(GroundworkRuntimeRowStore.Key(claimId));
        if (entry is null)
            return null;
        var claim = GroundworkV2ExecutableActivityTemplateStorageConventions.DeserializeClaim(entry.Values.Values);
        if (!StringComparer.Ordinal.Equals(claim.TemplateHash, templateHash))
            throw new InvalidDataException(
                $"Groundwork executable activity template hash claim physical identity collision detected for '{templateHash}'.");
        return claim;
    }

    private static void EnsureOwnedClaim(
        ExecutableActivityTemplate template,
        GroundworkV2ExecutableActivityTemplateStorageConventions.TemplateHashClaim? claim)
    {
        if (claim is null)
            throw new InvalidDataException(
                $"Executable activity template '{template.TemplateId}' is missing its hash claim.");
        if (!StringComparer.Ordinal.Equals(claim.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(claim.TemplateId, template.TemplateId))
        {
            throw new InvalidDataException(
                $"Executable activity template '{template.TemplateId}' does not own its hash claim.");
        }
    }

    private static void EnsureSameIdentityAndContent(
        ExecutableActivityTemplate existing,
        ExecutableActivityTemplate candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.TemplateHash, candidate.TemplateHash))
            throw new InvalidOperationException(
                $"Template id '{candidate.TemplateId}' is already bound to hash '{existing.TemplateHash}', not '{candidate.TemplateHash}'.");

        var existingJson = ComparableContent(existing);
        var candidateJson = ComparableContent(candidate);
        if (!JsonNode.DeepEquals(existingJson, candidateJson))
        {
            throw new InvalidOperationException(
                $"Template id '{candidate.TemplateId}' and hash '{candidate.TemplateHash}' are already bound to different content.");
        }
    }

    private static JsonNode ComparableContent(ExecutableActivityTemplate template)
    {
        var json = JsonNode.Parse(GroundworkV2RuntimeJson.Serialize(template))?.AsObject()
                   ?? throw new InvalidDataException("Executable activity template content could not be compared.");
        json.Remove("createdAt");
        json.Remove("nodesById");
        return json;
    }

    private static InvalidOperationException HashCollision(
        ExecutableActivityTemplate template,
        string existingTemplateId) =>
        new(
            $"Template hash '{template.TemplateHash}' is already bound to id '{existingTemplateId}', not '{template.TemplateId}'.");



    private IUnitOfWork BeginAtomicUnitOfWork() => BeginAtomicUnitOfWork([
            ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
            ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind
        ]);

    private async ValueTask<BatchWriteReport> CommitAsync(
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await unitOfWork.CommitWithOutcomesAsync(cancellationToken);
            if (!report.IsSuccessful)
            {
                try
                {
                    unitOfWork.Rollback();
                }
                catch
                {
                    // Preserve the provider's attributed row outcomes.
                }
            }
            return report;
        }
        catch
        {
            try
            {
                unitOfWork.Rollback();
            }
            catch
            {
                // Preserve the provider's original failure.
            }

            throw;
        }
    }

    private static void StageDelete(
        IUnitOfWork unitOfWork,
        StorageUnit unit,
        string physicalId,
        StoredEntry entry)
    {
        var version = entry.Version ?? throw new InvalidDataException(
            $"Groundwork row in unit '{unit.Id.Value}' did not expose an optimistic revision.");
        unitOfWork.Stage(RowWrite.Delete(
            unit,
            GroundworkRuntimeRowStore.Key(physicalId),
            WriteOptions.IfVersion(version)));
    }

    private void RequireAtomicCommit()
    {
        if (!HasAtomicCommit)
            throw new NotSupportedException(
                "Groundwork executable activity template creation and deletion require the provider's evidenced atomic-commit capability.");
    }
}
