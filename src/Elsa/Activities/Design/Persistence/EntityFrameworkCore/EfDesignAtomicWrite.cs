using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

/// <summary>
/// Replacement contract for the EF Activities Design atomic-operation boundary. A host may
/// register one custom implementation before composing the EF backend.
/// </summary>
[ActivityDesignPersistenceReplacementContract]
public interface IDesignAtomicWriter
{
    Task<EfDesignAtomicWriteResult> ExecuteAsync(
        EfDesignAtomicWriteRequest request,
        Func<EfDesignAtomicWriteContext, CancellationToken, Task<EfDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default);

    Task<EfDesignAtomicWriteResult> ExecuteAsync(
        EfDesignAtomicWriteRequest request,
        Func<CancellationToken, Task>? beforeAttempt,
        Func<EfDesignAtomicWriteContext, CancellationToken, Task<EfDesignAtomicWriteStageResult>> stage,
        CancellationToken cancellationToken = default);
}

public sealed record EfDesignOperationIdentity
{
    public EfDesignOperationIdentity(string operationKind, string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        OperationKind = operationKind;
        OperationKey = operationKey;
    }

    public string OperationKind { get; }
    public string OperationKey { get; }
}

public sealed record EfDesignAtomicWriteRequest
{
    public EfDesignAtomicWriteRequest(EfDesignOperationIdentity operation, string canonicalRequestFingerprint, IReadOnlyCollection<string> mutatedUnits, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestFingerprint);
        ArgumentNullException.ThrowIfNull(mutatedUnits);
        if (mutatedUnits.Count == 0 || mutatedUnits.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one mutated unit is required.", nameof(mutatedUnits));
        Operation = operation;
        CanonicalRequestFingerprint = canonicalRequestFingerprint;
        MutatedUnits = mutatedUnits.Distinct(StringComparer.Ordinal).ToArray();
        TenantId = tenantId;
    }

    public EfDesignOperationIdentity Operation { get; }
    public string CanonicalRequestFingerprint { get; }
    public IReadOnlyCollection<string> MutatedUnits { get; }
    public string? TenantId { get; }
}

public sealed class EfDesignAtomicWriteContext
{
    private readonly ActivitiesDesignDbContext db;

    public EfDesignAtomicWriteContext(ActivitiesDesignDbContext db) => this.db = db;

    public ActivitiesDesignDbContext Db => db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}

public sealed record EfDesignAtomicWriteStageResult(bool IsAccepted, string? AuthoritativeResultFingerprint, string? AuthoritativeResultJson)
{
    public static EfDesignAtomicWriteStageResult Accepted(string fingerprint, string json) => new(true, fingerprint, json);
    public static EfDesignAtomicWriteStageResult Rejected() => new(false, null, null);
}

public enum EfDesignAtomicWriteStatus { Committed, Reconciled, Replayed, Rejected, Conflict }

public sealed record EfDesignAtomicWriteResult(EfDesignAtomicWriteStatus Status, string? AuthoritativeResultFingerprint, string? AuthoritativeResultJson)
{
    public static EfDesignAtomicWriteResult Committed(string fingerprint, string json) => new(EfDesignAtomicWriteStatus.Committed, fingerprint, json);
    public static EfDesignAtomicWriteResult Reconciled(string fingerprint, string json) => new(EfDesignAtomicWriteStatus.Reconciled, fingerprint, json);
    public static EfDesignAtomicWriteResult Replayed(string fingerprint, string json) => new(EfDesignAtomicWriteStatus.Replayed, fingerprint, json);
    public static EfDesignAtomicWriteResult Conflict() => new(EfDesignAtomicWriteStatus.Conflict, null, null);
    public static EfDesignAtomicWriteResult Rejected() => new(EfDesignAtomicWriteStatus.Rejected, null, null);
}

/// <param name="transactionFactory">
/// Supplies the transaction an attempt runs in. It defaults to a new transaction on <paramref name="db"/>;
/// a caller that owns a wider transaction passes a non-owning handle so the operation runs inside it.
/// </param>
public sealed class EfDesignAtomicWrite(
    ActivitiesDesignDbContext db,
    IPersistenceAccessContextAccessor? accessContextAccessor = null,
    Func<CancellationToken, Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>>? transactionFactory = null) : IDesignAtomicWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<CancellationToken, Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>> beginTransaction =
        transactionFactory ?? (token => db.Database.BeginTransactionAsync(token));

    public Task<EfDesignAtomicWriteResult> ExecuteAsync(EfDesignAtomicWriteRequest request, Func<EfDesignAtomicWriteContext, CancellationToken, Task<EfDesignAtomicWriteStageResult>> stage, CancellationToken cancellationToken = default) =>
        ExecuteAsync(request, null, stage, cancellationToken);

    public async Task<EfDesignAtomicWriteResult> ExecuteAsync(EfDesignAtomicWriteRequest request, Func<CancellationToken, Task>? beforeAttempt, Func<EfDesignAtomicWriteContext, CancellationToken, Task<EfDesignAtomicWriteStageResult>> stage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stage);
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = request.TenantId ?? accessContextAccessor?.Current.Scope?.Value;
        accessContextAccessor?.Current.EnsureTenantScope(tenantId);
        var markerId = MarkerId(request.Operation, tenantId);
        var operationKindIdentityHash = ExactIdentityHash(request.Operation.OperationKind);
        var operationKeyIdentityHash = ExactIdentityHash(request.Operation.OperationKey);
        var scopeKey = ActivitiesDesignDbContext.NormalizeTenantKey(tenantId);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            var existing = await FindMarkerAsync(scopeKey, operationKindIdentityHash, operationKeyIdentityHash, cancellationToken);
            if (existing is not null)
                return Resolve(existing, request, tenantId, false);
            if (beforeAttempt is not null)
                await beforeAttempt(cancellationToken);

            transaction = await beginTransaction(cancellationToken);
            var marker = await db.ActivityDesignOperations.SingleOrDefaultAsync(x =>
                x.TenantScopeKey == scopeKey &&
                x.OperationKindIdentityHash == operationKindIdentityHash &&
                x.OperationKeyIdentityHash == operationKeyIdentityHash, cancellationToken);
            if (marker is not null)
            {
                await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
                db.ChangeTracker.Clear();
                return Resolve(marker, request, tenantId, false);
            }

            var trackedTenantsBeforeStage = db.ChangeTracker.Entries<Elsa.Primitives.Entities.TenantEntity>()
                .ToDictionary(entry => entry.Entity, entry => entry.State);
            var context = new EfDesignAtomicWriteContext(db);
            var staged = await stage(context, cancellationToken);
            ArgumentNullException.ThrowIfNull(staged);
            var stagedTenantIds = db.ChangeTracker.Entries<Elsa.Primitives.Entities.TenantEntity>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Where(entry => !trackedTenantsBeforeStage.TryGetValue(entry.Entity, out var previousState) || previousState != entry.State)
                .Select(entry => entry.Entity.TenantId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var enforceStagedTenant = tenantId is not null || accessContextAccessor?.Current.AcrossScopes == true || accessContextAccessor?.Current.IsGlobal == true;
            if (enforceStagedTenant && stagedTenantIds.Any(stagedTenantId => !StringComparer.Ordinal.Equals(stagedTenantId, tenantId)))
                throw new InvalidDataException("An EF design operation staged an entity owned by a different tenant.");
            if (!staged.IsAccepted)
            {
                await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
                db.ChangeTracker.Clear();
                return EfDesignAtomicWriteResult.Rejected();
            }
            if (string.IsNullOrWhiteSpace(staged.AuthoritativeResultFingerprint) || staged.AuthoritativeResultJson is null)
                throw new InvalidDataException("An accepted EF design operation must provide an authoritative result.");
            db.ActivityDesignOperations.Add(new ActivityDesignOperationRecord
            {
                Id = markerId,
                OperationKind = request.Operation.OperationKind,
                OperationKey = request.Operation.OperationKey,
                OperationKindIdentityHash = operationKindIdentityHash,
                OperationKeyIdentityHash = operationKeyIdentityHash,
                TenantId = tenantId,
                TenantScopeKey = scopeKey,
                CanonicalRequestFingerprint = request.CanonicalRequestFingerprint,
                AuthoritativeResultFingerprint = staged.AuthoritativeResultFingerprint,
                AuthoritativeResultJson = staged.AuthoritativeResultJson,
                MutatedUnitsJson = SerializeOrThrow(request.MutatedUnits, "atomic-write")
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return EfDesignAtomicWriteResult.Committed(staged.AuthoritativeResultFingerprint, staged.AuthoritativeResultJson);
        }
        catch (DbUpdateConcurrencyException) when (transaction is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (transaction is not null && EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            var winner = await FindMarkerAsync(scopeKey, operationKindIdentityHash, operationKeyIdentityHash, CancellationToken.None);
            if (winner is not null)
                return Resolve(winner, request, tenantId, true);

            // Entries on a relational DbUpdateException describe the failing batch, not
            // necessarily the constraint which failed. The marker is therefore classified only
            // after an exact re-read; otherwise this is an ordinary provider write failure.
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "atomic-write", request.Operation.OperationKind, exception);
        }
        catch (OperationCanceledException) when (transaction is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (DesignPersistenceException) when (transaction is not null)
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not DesignPersistenceException)
        {
            if (transaction is not null) await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            // A provider can report an error after the transaction has durably committed. The
            // operation marker is the only safe authority for classifying that ambiguity. Do not
            // infer it from DbUpdateException.Entries: a single EF batch can contain an unrelated
            // unique failure and the operation marker together.
            try
            {
                var winner = await FindMarkerAsync(scopeKey, operationKindIdentityHash, operationKeyIdentityHash, CancellationToken.None);
                if (winner is not null)
                    return Resolve(winner, request, tenantId, true);
            }
            catch (Exception readException) when (EfPersistenceCleanup.IsCatchable(readException))
            {
                // Reconciliation is best effort; preserve the original operation failure when
                // the provider cannot be queried after the failed write.
            }
            if (exception is DbException or DbUpdateException)
                throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "atomic-write", request.Operation.OperationKind, exception);
            if (exception is InvalidDataException or JsonException)
                throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "atomic-write", request.Operation.OperationKind, exception);
            throw;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private static EfDesignAtomicWriteResult Resolve(ActivityDesignOperationRecord marker, EfDesignAtomicWriteRequest request, string? tenantId, bool reconciled)
    {
        if (!StringComparer.Ordinal.Equals(marker.TenantId, tenantId) || !StringComparer.Ordinal.Equals(marker.TenantScopeKey, ActivitiesDesignDbContext.NormalizeTenantKey(tenantId)) || !StringComparer.Ordinal.Equals(marker.OperationKind, request.Operation.OperationKind) || !StringComparer.Ordinal.Equals(marker.OperationKey, request.Operation.OperationKey))
            throw new InvalidDataException("The EF design operation marker identity does not match its key.");
        if (!StringComparer.Ordinal.Equals(marker.CanonicalRequestFingerprint, request.CanonicalRequestFingerprint) ||
            !StringComparer.Ordinal.Equals(marker.MutatedUnitsJson, SerializeOrThrow(request.MutatedUnits, "atomic-replay")))
            return EfDesignAtomicWriteResult.Conflict();
        return reconciled
            ? EfDesignAtomicWriteResult.Reconciled(marker.AuthoritativeResultFingerprint, marker.AuthoritativeResultJson)
            : EfDesignAtomicWriteResult.Replayed(marker.AuthoritativeResultFingerprint, marker.AuthoritativeResultJson);
    }

    private static string MarkerId(EfDesignOperationIdentity operation, string? tenantId)
    {
        var material = $"elsa-design-operation:ef-v2|scope={Encode(tenantId)}|kind={Encode(operation.OperationKind)}|key={Encode(operation.OperationKey)}";
        return $"design-operation-ef-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }

    private static string Encode(string? value) => value is null
        ? "-1:"
        : $"{value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{value}";

    private Task<ActivityDesignOperationRecord?> FindMarkerAsync(string scopeKey, string operationKindIdentityHash, string operationKeyIdentityHash, CancellationToken cancellationToken) =>
        db.ActivityDesignOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantScopeKey == scopeKey &&
            x.OperationKindIdentityHash == operationKindIdentityHash &&
            x.OperationKeyIdentityHash == operationKeyIdentityHash, cancellationToken);

    private static string ExactIdentityHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string SerializeOrThrow<T>(T value, string operation)
    {
        try { return JsonSerializer.Serialize(value, Json); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "save", operation, exception); }
    }

}

/// <summary>Shared command adapter that canonicalizes request/result material and translates replay outcomes.</summary>
public static class EfDesignAtomicCommand
{
    private const string MaterialSchemaVersion = "1";

    public static async Task<EfDesignAtomicCommandResult<TResult>> ExecuteAsync<TRequest, TResult>(
        IDesignAtomicWriter atomicWrite,
        Elsa.Workflows.Design.Persistence.Core.Models.DesignOperationKey operationKey,
        string operationKind,
        TRequest requestMaterial,
        IReadOnlyCollection<string> mutatedUnits,
        Func<EfDesignAtomicWriteContext, CancellationToken, Task<TResult>> stage,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default,
        string? tenantId = null)
        where TRequest : notnull
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(atomicWrite);
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
        ArgumentNullException.ThrowIfNull(requestMaterial);
        ArgumentNullException.ThrowIfNull(mutatedUnits);
        ArgumentNullException.ThrowIfNull(stage);
        var options = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var requestJson = SerializeOrThrow(new { Kind = operationKind, Version = MaterialSchemaVersion, Value = requestMaterial }, options, operationKind);
        var result = await atomicWrite.ExecuteAsync(
            new(new EfDesignOperationIdentity(operationKind, operationKey.Value), Fingerprint(requestJson), mutatedUnits, tenantId),
            async (context, token) =>
            {
                var value = await stage(context, token);
                var json = SerializeOrThrow(value, options, operationKind);
                return EfDesignAtomicWriteStageResult.Accepted(Fingerprint(json), json);
            }, cancellationToken);
        return result.Status switch
        {
            EfDesignAtomicWriteStatus.Committed or EfDesignAtomicWriteStatus.Reconciled or EfDesignAtomicWriteStatus.Replayed =>
                new(DeserializeOrThrow<TResult>(result.AuthoritativeResultJson!, options, operationKind), result.Status),
            EfDesignAtomicWriteStatus.Conflict => throw new DesignPersistenceOperationConflictException(operationKind, operationKey.Value),
            EfDesignAtomicWriteStatus.Rejected => throw new DesignPersistenceOperationRejectedException(operationKind, operationKey.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
        };
    }

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string SerializeOrThrow<T>(T value, JsonSerializerOptions options, string operation)
    {
        try { return JsonSerializer.Serialize(value, options); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "save", operation, exception); }
    }

    private static T DeserializeOrThrow<T>(string value, JsonSerializerOptions options, string operation) where T : notnull
    {
        try { return JsonSerializer.Deserialize<T>(value, options) ?? throw new InvalidDataException("The authoritative design result is unreadable."); }
        catch (DesignPersistenceException) { throw; }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or InvalidDataException)
        { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "load", operation, exception); }
    }
}

public sealed record EfDesignAtomicCommandResult<TResult>(TResult Value, EfDesignAtomicWriteStatus Status) where TResult : notnull;

public sealed class DesignPersistenceOperationConflictException(string kind, string key)
    : InvalidOperationException($"Design operation key '{key}' is already bound to different material for '{kind}'.");

public sealed class DesignPersistenceOperationRejectedException(string kind, string key)
    : InvalidOperationException($"Design operation '{kind}' with key '{key}' was rejected and rolled back.");
