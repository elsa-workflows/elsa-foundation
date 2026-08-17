using System.Text.Json.Nodes;
using Elsa.Persistence.Core;
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
public sealed class GroundworkV2ExecutableActivityTemplateStore : IExecutableActivityTemplateStore
{
    private const int MaximumDeleteAttempts = 8;

    private readonly IGroundworkStorageSessionSource sessions;
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly string? targetName;
    private readonly StorageUnit templateUnit;
    private readonly StorageUnit claimUnit;

    public GroundworkV2ExecutableActivityTemplateStore(
        IGroundworkStorageSessionSource sessions,
        IPersistenceAccessContextAccessor accessContextAccessor,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(accessContextAccessor);
        this.sessions = sessions;
        this.accessContextAccessor = accessContextAccessor;
        this.targetName = targetName;
        templateUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind, targetName);
        claimUnit = sessions.Unit(ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind, targetName);
    }

    public async ValueTask SaveAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken = default)
    {
        GroundworkV2ExecutableActivityTemplateStorageConventions.Validate(template);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Access;
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
        var entry = OpenTemplate().Read(GroundworkRuntimeRowStore.Key(physicalId));
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
        var table = new TableId(templateUnit.Name);
        var hash = Column(table, ElsaRuntimeV2StorageManifest.TemplateHashField);
        var templateId = Column(table, ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateIdField);
        var result = OpenTemplate().Query(new QueryRequest(
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
        var table = new TableId(templateUnit.Name);
        var collection = Column(table, ElsaRuntimeV2StorageManifest.CollectionField);
        var templateId = Column(table, ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateIdField);
        var result = OpenTemplate().Query(new QueryRequest(
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
        _ = Access;
        RequireAtomicCommit();

        for (var attempt = 0; attempt < MaximumDeleteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var templateEntry = OpenTemplate().Read(GroundworkRuntimeRowStore.Key(physicalId));
            if (templateEntry is null)
                return false;

            var template = GroundworkV2ExecutableActivityTemplateStorageConventions.Deserialize(templateEntry.Values.Values);
            if (!StringComparer.Ordinal.Equals(template.TemplateId, templateId))
                throw new InvalidDataException(
                    $"Groundwork executable activity template physical identity collision detected for '{templateId}'.");

            var claimEntry = OpenClaim().Read(GroundworkRuntimeRowStore.Key(
                GroundworkV2ExecutableActivityTemplateStorageConventions.HashClaimId(template.TemplateHash)));
            if (claimEntry is not null)
            {
                var claim = GroundworkV2ExecutableActivityTemplateStorageConventions.DeserializeClaim(claimEntry.Values.Values);
                EnsureOwnedClaim(template, claim);
            }

            using var unitOfWork = BeginAtomicUnitOfWork();
            StageDelete(unitOfWork, templateUnit, physicalId, templateEntry);
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

    private async ValueTask<BatchWriteReport> TryCreateAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken)
    {
        using var unitOfWork = BeginAtomicUnitOfWork();
        unitOfWork.Stage(RowWrite.Insert(
            templateUnit,
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
        var entry = OpenClaim().Read(GroundworkRuntimeRowStore.Key(claimId));
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

    private IStorageSession OpenTemplate() => sessions.Open(templateUnit.Id.Value, Access, targetName);

    private IStorageSession OpenClaim() => sessions.Open(claimUnit.Id.Value, Access, targetName);

    private IUnitOfWork BeginAtomicUnitOfWork() => sessions.BeginUnitOfWork(
        Access,
        BatchWriteOptions.Exact,
        [
            ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
            ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind
        ],
        targetName);

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

    private StorageAccess Access
    {
        get
        {
            var context = accessContextAccessor.Current;
            if (context.Scope is null || context.AcrossScopes)
            {
                throw new InvalidOperationException(
                    "Groundwork executable activity templates require one explicit persistence scope; global and across-scope access are refused.");
            }

            return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        }
    }

    private void RequireAtomicCommit()
    {
        if (sessions is not IGroundworkStorageCapabilitySource capabilitySource ||
            !capabilitySource.Capabilities(targetName).Any(capability =>
                capability.Id.Equals(WellKnownCapabilities.AtomicCommit)))
        {
            throw new NotSupportedException(
                "Groundwork executable activity template creation and deletion require the provider's evidenced atomic-commit capability.");
        }
    }

    private ColumnRef Column(TableId table, string name)
    {
        var definition = templateUnit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork executable activity template unit '{templateUnit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            PortableType.Boolean => QueryType.Boolean,
            _ => throw new InvalidOperationException(
                $"Groundwork executable activity template query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(table, name, type, definition.IsNullable, definition.MaxLength);
    }

    private static Predicate Equal(ColumnRef column, string value) =>
        new Predicate.Equal(column, QueryConstant.Of(column, value));

    private static Paging PagingFor(int limit, string? continuationToken) =>
        continuationToken is null
            ? Paging.Keyset(limit)
            : Paging.Continuation(continuationToken, limit);
}
