using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfWorkflowExecutableSourceReferenceStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor access,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IWorkflowExecutableSourceReferenceStore
{
    private const string ContinuationPurpose = "ef-runtime-source-reference-page-v1";
    private readonly IRuntimeRecoveryContinuationCodec continuationCodec = continuationCodec;
    public async ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
    {
        Validate(reference);
        var scope = ScopeForWrite(reference);
        var id = CreateId(scope, reference.SourceReferenceId);
        context.ChangeTracker.Clear();
        await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "saving",
            reference.SourceReferenceId,
            () => context.Database.BeginTransactionAsync(cancellationToken));
        try
        {
            var existing = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context,
                "saving",
                reference.SourceReferenceId,
                () => context.WorkflowExecutableSourceReferences.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.Id == id &&
                    x.ScopeKeyHash == Hash(scope) &&
                    x.ScopeKey == Encode(scope) &&
                    x.SourceReferenceIdHash == Hash(reference.SourceReferenceId) &&
                    x.SourceReferenceId == Encode(reference.SourceReferenceId),
                    cancellationToken));
            if (existing is not null)
            {
                _ = Read(existing, scope, reference.SourceReferenceId);
                throw new InvalidOperationException($"Workflow executable source reference '{reference.SourceReferenceId}' already exists; source references are create-only.");
            }
            context.WorkflowExecutableSourceReferences.Add(ToEntity(reference, scope, id));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "saving", reference.SourceReferenceId, () => context.SaveChangesAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "saving", reference.SourceReferenceId, () => transaction.CommitAsync(cancellationToken));
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
        { context.ChangeTracker.Clear(); throw new InvalidOperationException("The workflow executable source reference already exists; source references are create-only.", exception); }
        catch (DbUpdateException exception)
        { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("saving", reference.SourceReferenceId, exception); }
        catch (DbException exception)
        { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("saving", reference.SourceReferenceId, exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        var scope = RequireScope();
        var id = CreateId(scope, sourceReferenceId);
        var row = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "finding",
            sourceReferenceId,
            () => context.WorkflowExecutableSourceReferences.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == id &&
                x.ScopeKeyHash == Hash(scope) &&
                x.ScopeKey == Encode(scope) &&
                x.SourceReferenceIdHash == Hash(sourceReferenceId) &&
                x.SourceReferenceId == Encode(sourceReferenceId),
                cancellationToken));
        return row is null ? null : Read(row, scope, sourceReferenceId);
    }

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(
        WorkflowExecutableSourceReferenceArtifactPageQuery query,
        CancellationToken cancellationToken = default) =>
        Page(query, Route.Artifact, query.ArtifactId, null, null, cancellationToken);

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByDefinitionVersionPageAsync(
        WorkflowExecutableSourceReferenceDefinitionVersionPageQuery query,
        CancellationToken cancellationToken = default) =>
        Page(query, Route.DefinitionVersion, null, query.DefinitionVersionId, null, cancellationToken);

    public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(
        WorkflowExecutableSourceReferencePageQuery query,
        CancellationToken cancellationToken = default) =>
        Page(query, Route.All, null, null, query, cancellationToken);

    private async ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> Page(RuntimeStorePageRequest request, Route route, string? artifact, string? definition, WorkflowExecutableSourceReferencePageQuery? all, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var accessContext = access.Current;
        var acrossScopes = accessContext.AcrossScopes;
        if (acrossScopes && route != Route.DefinitionVersion)
            throw new InvalidOperationException("EF workflow executable source-reference paging permits across-scope access only for definition-version queries.");
        var scope = accessContext.Scope?.Value;
        if (!acrossScopes && scope is null)
            throw new InvalidOperationException("EF workflow executable source-reference paging requires one explicit persistence scope.");
        var binding = BuildBinding(route, scope, artifact, definition, all, accessContext);
        var cursor = Decode(request.ContinuationToken, binding);
        var query = context.WorkflowExecutableSourceReferences.AsNoTracking().AsQueryable();
        if (!acrossScopes)
            query = query.Where(x => x.ScopeKeyHash == Hash(scope!) && x.ScopeKey == Encode(scope!));
        if (artifact is not null)
            query = query.Where(x => x.ArtifactIdHash == EfRelationalIdentity.Hash(artifact) && x.ArtifactId == Encode(artifact));
        if (definition is not null)
            query = query.Where(x => x.DefinitionVersionIdHash == EfRelationalIdentity.Hash(definition) && x.DefinitionVersionId == Encode(definition));
        if (all?.Scope is { } sourceScope)
            query = query.Where(x => x.Scope == sourceScope.ToString());
        if (all?.LiveOnly == true)
            query = query.Where(x => !x.IsRetired && x.ExpiresAtUtcTicks > all.Now!.Value.UtcTicks);
        if (cursor is not null)
            query = acrossScopes
                ? query.Where(x =>
                    x.ScopeKeyOrderKey.CompareTo(cursor.ScopeKeyOrderKey) > 0 ||
                    x.ScopeKeyOrderKey == cursor.ScopeKeyOrderKey &&
                    (x.SourceReferenceIdOrderKey.CompareTo(cursor.Key) > 0 ||
                     x.SourceReferenceIdOrderKey == cursor.Key && x.Id.CompareTo(cursor.Id) > 0))
                : query.Where(x => x.SourceReferenceIdOrderKey.CompareTo(cursor.Key) > 0);
        var ordered = acrossScopes
            ? query.OrderBy(x => x.ScopeKeyOrderKey).ThenBy(x => x.SourceReferenceIdOrderKey).ThenBy(x => x.Id)
            : query.OrderBy(x => x.SourceReferenceIdOrderKey).ThenBy(x => x.Id);
        var rows = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "listing",
            binding,
            () => ordered.Take(request.Limit + 1).ToArrayAsync(cancellationToken));
        var hasMore = rows.Length > request.Limit;
        if (hasMore)
            rows = rows[..request.Limit];
        var items = rows.Select(x => Read(x, acrossScopes ? EfRelationalIdentity.Decode(x.ScopeKey) : scope!, Decode(x.SourceReferenceId))).ToArray();
        var next = hasMore
            ? EncodeCursor(binding, rows[^1].ScopeKeyOrderKey, rows[^1].SourceReferenceIdOrderKey, rows[^1].Id)
            : null;
        return new RuntimeStorePage<WorkflowExecutableSourceReference>(request, items, next);
    }

    public async ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        var scope = RequireScope();
        try
        {
            var row = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context,
                "retiring",
                sourceReferenceId,
                () => context.WorkflowExecutableSourceReferences.SingleOrDefaultAsync(x =>
                    x.Id == CreateId(scope, sourceReferenceId) &&
                    x.ScopeKeyHash == Hash(scope) &&
                    x.ScopeKey == Encode(scope) &&
                    x.SourceReferenceIdHash == Hash(sourceReferenceId) &&
                    x.SourceReferenceId == Encode(sourceReferenceId),
                    cancellationToken));
            if (row is null)
            {
                context.ChangeTracker.Clear();
                return false;
            }
            var current = Read(row, scope, sourceReferenceId);
            if (current.DeletedAt is not null)
            {
                context.ChangeTracker.Clear();
                return true;
            }
            Copy(row, current.Retire(deletedAt, reason), scope);
            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context, "retiring", sourceReferenceId, () => context.Database.BeginTransactionAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "retiring", sourceReferenceId, () => context.SaveChangesAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "retiring", sourceReferenceId, () => transaction.CommitAsync(cancellationToken));
            context.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
        catch (DbUpdateException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("retiring", sourceReferenceId, exception); }
        catch (DbException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("retiring", sourceReferenceId, exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public ValueTask<bool> TryRetireAsync(
        WorkflowExecutableSourceReference expectedLiveReference,
        WorkflowExecutableSourceReference retiredReference,
        CancellationToken cancellationToken = default) =>
        TryReplace(expectedLiveReference, retiredReference, false, cancellationToken);

    public ValueTask<bool> TryRestoreAsync(
        WorkflowExecutableSourceReference expectedRetiredReference,
        WorkflowExecutableSourceReference restoredReference,
        CancellationToken cancellationToken = default) =>
        TryReplace(expectedRetiredReference, restoredReference, true, cancellationToken);

    private async ValueTask<bool> TryReplace(WorkflowExecutableSourceReference expected, WorkflowExecutableSourceReference replacement, bool restore, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        Validate(expected);
        Validate(replacement);
        var valid = WorkflowExecutableSourceReferenceComparer.SameIdentity(expected, replacement) &&
                    (restore
                        ? expected.DeletedAt is not null && replacement.DeletedAt is null
                        : expected.DeletedAt is null && replacement.DeletedAt is not null);
        if (!valid)
            return false;
        var scope = RequireScope();
        try
        {
            var row = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context,
                restore ? "restoring" : "retiring",
                expected.SourceReferenceId,
                () => context.WorkflowExecutableSourceReferences.SingleOrDefaultAsync(x =>
                    x.Id == CreateId(scope, expected.SourceReferenceId) &&
                    x.ScopeKeyHash == Hash(scope) &&
                    x.ScopeKey == Encode(scope) &&
                    x.SourceReferenceIdHash == Hash(expected.SourceReferenceId) &&
                    x.SourceReferenceId == Encode(expected.SourceReferenceId),
                    cancellationToken));
            if (row is null)
            {
                context.ChangeTracker.Clear();
                return false;
            }
            var current = Read(row, scope, expected.SourceReferenceId);
            if (!WorkflowExecutableSourceReferenceComparer.SameSnapshot(current, expected))
            {
                context.ChangeTracker.Clear();
                return false;
            }
            Copy(row, replacement, scope);
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context,
                restore ? "restoring" : "retiring",
                expected.SourceReferenceId,
                () => context.SaveChangesAsync(cancellationToken));
            context.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
        catch (DbUpdateException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure(restore ? "restoring" : "retiring", expected.SourceReferenceId, exception); }
        catch (DbException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure(restore ? "restoring" : "retiring", expected.SourceReferenceId, exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);
        var scope = RequireScope();
        try
        {
            var row = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context,
                "deleting",
                sourceReferenceId,
                () => context.WorkflowExecutableSourceReferences.SingleOrDefaultAsync(x =>
                    x.Id == CreateId(scope, sourceReferenceId) &&
                    x.ScopeKeyHash == Hash(scope) &&
                    x.ScopeKey == Encode(scope) &&
                    x.SourceReferenceIdHash == Hash(sourceReferenceId) &&
                    x.SourceReferenceId == Encode(sourceReferenceId),
                    cancellationToken));
            if (row is null)
            {
                context.ChangeTracker.Clear();
                return false;
            }
            _ = Read(row, scope, sourceReferenceId);
            context.Remove(row);
            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context, "deleting", sourceReferenceId, () => context.Database.BeginTransactionAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "deleting", sourceReferenceId, () => context.SaveChangesAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "deleting", sourceReferenceId, () => transaction.CommitAsync(cancellationToken));
            context.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
        catch (DbUpdateException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("deleting", sourceReferenceId, exception); }
        catch (DbException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("deleting", sourceReferenceId, exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        ArgumentNullException.ThrowIfNull(batch);
        var scope = RequireScope();
        try
        {
            var query = context.WorkflowExecutableSourceReferences.Where(x => x.ExpiresAtUtcTicks <= now.UtcTicks || x.IsRetired);
            query = query.Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope));
            var rows = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context,
                "cleaning up",
                scope,
                () => query.OrderBy(x => x.ExpiresAtUtcTicks).ThenBy(x => x.SourceReferenceIdOrderKey).Take(batch.Limit).ToArrayAsync(cancellationToken));
            var deleted = new List<string>(rows.Length);
            foreach (var row in rows)
            {
                var current = Read(row, scope, Decode(row.SourceReferenceId));
                if (await TryDeleteCapturedAsync(current.SourceReferenceId, row.IncarnationId, row.Revision, cancellationToken))
                    deleted.Add(current.SourceReferenceId);
            }
            context.ChangeTracker.Clear();
            return deleted;
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("cleaning up", scope, exception);
        }
        catch (DbException exception)
        {
            context.ChangeTracker.Clear();
            throw NormalizeProviderFailure("cleaning up", scope, exception);
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private async ValueTask<bool> TryDeleteCapturedAsync(string sourceReferenceId, string expectedIncarnationId, long expectedRevision, CancellationToken cancellationToken)
    {
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var row = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "cleaning up",
            sourceReferenceId,
            () => context.WorkflowExecutableSourceReferences.SingleOrDefaultAsync(x =>
                x.Id == CreateId(scope, sourceReferenceId) &&
                x.ScopeKeyHash == Hash(scope) &&
                x.ScopeKey == Encode(scope) &&
                x.SourceReferenceIdHash == Hash(sourceReferenceId) &&
                x.SourceReferenceId == Encode(sourceReferenceId),
                cancellationToken));
        if (row is null || row.IncarnationId != expectedIncarnationId || row.Revision != expectedRevision)
        {
            context.ChangeTracker.Clear();
            return false;
        }
        _ = Read(row, scope, sourceReferenceId);
        context.Remove(row);
        try
        {
            await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context, "cleaning up", sourceReferenceId, () => context.Database.BeginTransactionAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "cleaning up", sourceReferenceId, () => context.SaveChangesAsync(cancellationToken));
            await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                context, "cleaning up", sourceReferenceId, () => transaction.CommitAsync(cancellationToken));
            context.ChangeTracker.Clear();
            return true;
        }
        catch (DbUpdateConcurrencyException) { context.ChangeTracker.Clear(); return false; }
        catch (DbUpdateException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("cleaning up", sourceReferenceId, exception); }
        catch (DbException exception) { context.ChangeTracker.Clear(); throw NormalizeProviderFailure("cleaning up", sourceReferenceId, exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var scopeHash = Hash(scope);
        var scopeKey = Encode(scope);
        var unreferenced = new List<string>(candidates.ArtifactIds.Count);
        foreach (var candidate in candidates.ArtifactIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidateHash = Hash(candidate);
            var referenced = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                context,
                "checking references",
                candidate,
                () => context.WorkflowExecutableSourceReferences
                    .AsNoTracking()
                    .Where(x =>
                        x.ScopeKeyHash == scopeHash &&
                        x.ScopeKey == scopeKey &&
                        !x.IsRetired &&
                        x.ExpiresAtUtcTicks > now.UtcTicks &&
                        x.ArtifactIdHash == candidateHash &&
                        x.ArtifactId == Encode(candidate))
                    .OrderBy(x => x.SourceReferenceIdOrderKey)
                    .Select(x => x.Id)
                    .Take(1)
                    .ToArrayAsync(cancellationToken));
            if (referenced.Length == 0)
                unreferenced.Add(candidate);
        }

        return unreferenced;
    }

    private string RequireScope()
    {
        var current = access.Current;
        if (current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF workflow executable source-reference persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    private string ScopeForRead() => RequireScope();
    private string ScopeForWrite(WorkflowExecutableSourceReference reference)
    {
        var scope = RequireScope();
        if (reference.TenantId is not null)
            access.Current.EnsureTenantScope(reference.TenantId);
        return scope;
    }
    private static WorkflowExecutableSourceReferenceEntity ToEntity(WorkflowExecutableSourceReference value, string scope, string id)
    {
        var row = new WorkflowExecutableSourceReferenceEntity { Id = id, IncarnationId = NewIncarnationId() };
        Copy(row, value, scope);
        return row;
    }
    private static void Copy(WorkflowExecutableSourceReferenceEntity row, WorkflowExecutableSourceReference value, string scope)
    {
        row.SourceReferenceId = Encode(value.SourceReferenceId);
        row.SourceReferenceIdHash = Hash(value.SourceReferenceId);
        row.SourceReferenceIdOrderKey = OrderKey(value.SourceReferenceId);
        row.ArtifactId = Encode(value.ArtifactId);
        row.ArtifactIdHash = Hash(value.ArtifactId);
        row.DefinitionVersionId = Encode(value.DefinitionVersionId);
        row.DefinitionVersionIdHash = Hash(value.DefinitionVersionId);
        row.DefinitionId = Encode(value.DefinitionId);
        row.DefinitionIdHash = Hash(value.DefinitionId);
        row.ScopeKey = Encode(scope);
        row.ScopeKeyHash = Hash(scope);
        row.ScopeKeyOrderKey = ScopeOrderKey(scope);
        row.Scope = value.Scope.ToString();
        row.IsRetired = value.DeletedAt is not null;
        row.ExpiresAtUtcTicks = (value.ExpiresAt ?? DateTimeOffset.MaxValue).UtcTicks;
        row.ContentJson = SerializeEnvelope(value);
        row.SchemaVersion = RuntimeArtifactEfModule.SchemaVersion;
        if (row.Revision <= 0)
            row.Revision = 1;
        else
            row.Revision++;
    }
    private static WorkflowExecutableSourceReference Read(WorkflowExecutableSourceReferenceEntity row, string expectedScope, string expectedId)
    {
        try
        {
            if (row.Id != CreateId(expectedScope, expectedId) ||
                row.SourceReferenceId != Encode(expectedId) ||
                row.SourceReferenceIdHash != Hash(expectedId) ||
                row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion ||
                row.SourceReferenceIdOrderKey != OrderKey(expectedId) ||
                row.ScopeKey != Encode(expectedScope) ||
                row.ScopeKeyHash != Hash(expectedScope) ||
                row.ScopeKeyOrderKey != ScopeOrderKey(expectedScope) ||
                row.ArtifactIdHash != Hash(Decode(row.ArtifactId)) ||
                row.DefinitionIdHash != Hash(Decode(row.DefinitionId)) ||
                row.DefinitionVersionIdHash != Hash(Decode(row.DefinitionVersionId)) ||
                row.Revision <= 0 || string.IsNullOrWhiteSpace(row.IncarnationId))
                throw new InvalidDataException("The persisted workflow executable source reference row is corrupt.");
            var envelope = JsonNode.Parse(row.ContentJson)?.AsObject()
                           ?? throw new InvalidDataException("The persisted workflow executable source reference envelope is empty.");
            if (!StringComparer.Ordinal.Equals(ReadString(envelope, "collection"), "workflowExecutableSourceReference") ||
                !StringComparer.Ordinal.Equals(Decode(ReadString(envelope, "artifactId")), Decode(row.ArtifactId)) ||
                envelope["reference"] is null)
                throw new InvalidDataException("The persisted workflow executable source reference envelope projection is corrupt.");
            var value = RuntimeArtifactJson.Deserialize<WorkflowExecutableSourceReference>(envelope["reference"]!.ToJsonString());
            Validate(value);
            if ((value.TenantId is not null && value.TenantId != expectedScope) ||
                EfRelationalIdentity.Decode(row.ScopeKey) != expectedScope ||
                value.SourceReferenceId != expectedId ||
                value.ArtifactId != Decode(row.ArtifactId) ||
                value.DefinitionId != Decode(row.DefinitionId) ||
                value.DefinitionVersionId != Decode(row.DefinitionVersionId) ||
                value.Scope.ToString() != row.Scope ||
                row.ExpiresAtUtcTicks != (value.ExpiresAt ?? DateTimeOffset.MaxValue).UtcTicks ||
                row.IsRetired != (value.DeletedAt is not null))
                throw new InvalidDataException("The persisted workflow executable source reference projection is corrupt.");
            return value;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException or OverflowException)
        {
            throw new InvalidDataException("The persisted workflow executable source reference payload is corrupt.", exception);
        }
    }
    private static void Validate(WorkflowExecutableSourceReference value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.SourceReferenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.SourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.DefinitionVersionId);
        ValidateBound(value.SourceReferenceId, nameof(value.SourceReferenceId));
        ValidateBound(value.ArtifactId, nameof(value.ArtifactId));
        ValidateBound(value.DefinitionId, nameof(value.DefinitionId));
        ValidateBound(value.DefinitionVersionId, nameof(value.DefinitionVersionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(value.ArtifactVersion);
        if (value.SourceVersion is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(value.SourceVersion);
        if (!Enum.IsDefined(value.Scope))
            throw new ArgumentOutOfRangeException(nameof(value.Scope));
    }

    private static void ValidateBound(string value, string parameterName)
    {
        if (value.Length > RuntimeArtifactEfModule.IdentityMaximumLength)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static string SerializeEnvelope(WorkflowExecutableSourceReference value)
    {
        var reference = JsonNode.Parse(RuntimeArtifactJson.Serialize(value))
                       ?? throw new InvalidDataException("The workflow executable source reference payload could not be serialized.");
        return new JsonObject
        {
            ["collection"] = "workflowExecutableSourceReference",
            ["artifactId"] = Encode(value.ArtifactId),
            ["reference"] = reference
        }.ToJsonString();
    }

    private static string ReadString(JsonObject node, string property) =>
        node[property] is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException($"The source-reference envelope is missing '{property}'.");
    private static string CreateId(string scope, string value) => Hash($"{scope.Length}:{scope}{value.Length}:{value}");
    private static string NewIncarnationId() => Guid.NewGuid().ToString("N");
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Decode(string value) => EfRelationalIdentity.Decode(value);
    private static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeArtifactEfModule.IdentityMaximumLength));

    // Scope values are opaque persistence partitions and are not subject to the artifact-id bound. The
    // trailing zero code unit makes the variable-length key preserve ordinal prefix ordering ("a" < "aa")
    // while the hexadecimal representation remains provider/collation stable.
    private static string ScopeOrderKey(string value)
    {
        var bytes = new byte[checked((value.Length + 1) * sizeof(char))];
        for (var index = 0; index < value.Length; index++)
        {
            bytes[index * sizeof(char)] = (byte)(value[index] >> 8);
            bytes[index * sizeof(char) + 1] = (byte)value[index];
        }

        return Convert.ToHexString(bytes);
    }

    private static RuntimeArtifactEntityFrameworkPersistenceException NormalizeProviderFailure(string operation, string identity, Exception inner) =>
        new(operation, identity, $"The EF runtime artifact store failed while {operation} workflow executable source reference '{identity}'.", inner);
    private string BuildBinding(Route route, string? scope, string? artifact, string? definition, WorkflowExecutableSourceReferencePageQuery? all, PersistenceAccessContext accessContext)
    {
        var shape = RuntimeArtifactJson.Serialize(new
        {
            Route = route.ToString(),
            Scope = scope is null ? null : Hash(scope),
            AccessPolicy = accessContext.AccessPolicy.ToString(),
            AcrossScopes = accessContext.AcrossScopes,
            Purpose = accessContext.Purpose?.Value,
            Artifact = artifact,
            Definition = definition,
            FilterScope = all?.Scope?.ToString(),
            all?.LiveOnly,
            NowTicks = all?.Now?.UtcTicks
        });
        return Hash(shape);
    }

    private string EncodeCursor(string binding, string scopeKey, string key, string id) => continuationCodec.Encode(ContinuationPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new Cursor(1, binding, scopeKey, key, id))));

    private Cursor? Decode(string? token, string binding)
    {
        if (token is null)
            return null;
        try
        {
            var cursor = RuntimeArtifactJson.Deserialize<Cursor>(Encoding.UTF8.GetString(continuationCodec.Decode(ContinuationPurpose, token)));
            if (cursor.Version != 1 || cursor.Binding != binding || string.IsNullOrWhiteSpace(cursor.ScopeKeyOrderKey) || string.IsNullOrWhiteSpace(cursor.Key) || string.IsNullOrWhiteSpace(cursor.Id))
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or JsonException or InvalidOperationException or OverflowException)
        { throw new ArgumentException("The source-reference continuation token is invalid.", nameof(token), exception); }
    }

    private enum Route { All, Artifact, DefinitionVersion }
    private sealed record Cursor(int Version, string Binding, string ScopeKeyOrderKey, string Key, string Id);
}
