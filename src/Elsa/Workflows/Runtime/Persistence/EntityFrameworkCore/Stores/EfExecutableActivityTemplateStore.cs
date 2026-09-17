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

public sealed class EfExecutableActivityTemplateStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IRuntimeRecoveryContinuationCodec continuationCodec) : IExecutableActivityTemplateStore
{
    private const int MaximumCreateAttempts = 3;
    private const int MaximumDeleteAttempts = 8;
    private const string ContinuationPurpose = "ef-runtime-template-page-v1";
    private readonly IRuntimeRecoveryContinuationCodec continuationCodec = continuationCodec;

    public async ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default)
    {
        Validate(template);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var identity = new TemplateIdentity(scope, template.TemplateId, template.TemplateHash);
        var json = SerializeEnvelope(template);

        for (var attempt = 0; attempt < MaximumCreateAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            try
            {
                await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                    context, "saving", template.TemplateId, () => context.Database.BeginTransactionAsync(cancellationToken));
                if (await StageCreateAsync(template, identity, json, cancellationToken))
                    await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                        context, "saving", template.TemplateId, () => context.SaveChangesAsync(cancellationToken));
                await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                    context, "saving", template.TemplateId, () => transaction.CommitAsync(cancellationToken));
                return;
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) || EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                if (attempt + 1 == MaximumCreateAttempts)
                    await ReconcileCreateAsync(template, identity, exception, cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("saving", template.TemplateId, exception);
            }
            catch (DbException exception)
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("saving", template.TemplateId, exception);
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }
    }

    /// <summary>The context this store reads and stages through.</summary>
    internal BookmarkStateDbContext Context => context;

    /// <summary>
    /// Stages the template and its hash claim in the caller's open transaction without saving. An identical
    /// template already present is content-addressed and needs nothing, so this returns <c>false</c>; a
    /// different template under the same id or hash is refused as a conflict.
    /// </summary>
    internal Task<bool> StageCreateAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken)
    {
        Validate(template);
        var identity = new TemplateIdentity(RequireScope(), template.TemplateId, template.TemplateHash);
        return StageCreateAsync(template, identity, SerializeEnvelope(template), cancellationToken);
    }

    private async Task<bool> StageCreateAsync(ExecutableActivityTemplate template, TemplateIdentity identity, string json, CancellationToken cancellationToken)
    {
        var current = await FindRowByIdAsync(identity, cancellationToken);
        var claim = await FindClaimRowAsync(identity, cancellationToken);
        var byHash = await FindRowsByHashAsync(identity, cancellationToken);
        if (current is null && (NamesTemplate(claim?.TemplateId, template) || byHash.Any(row => NamesTemplate(row.TemplateId, template))))
        {
            // A template row and its hash claim commit together, but under read-committed isolation a racing
            // identical create can commit between the id read and the claim and hash reads. Seeing this id's
            // claim or row after missing it by id is that race, not corruption or a collision: read the pair again.
            current = await FindRowByIdAsync(identity, cancellationToken);
            claim = await FindClaimRowAsync(identity, cancellationToken);
        }
        if (current is not null)
        {
            var existing = Read(current, identity);
            EnsureOwnedClaim(claim, identity);
            EnsureMatchingIncarnation(current, claim!);
            EnsureSameIdentityAndContent(existing, template);
            return false;
        }
        if (claim is not null)
        {
            var existingClaim = ReadClaim(claim, identity);
            if (existingClaim.TemplateId == template.TemplateId)
                throw new InvalidDataException("Executable activity template hash claim exists without its template row.");
            throw HashCollision(template, existingClaim.TemplateId);
        }
        if (byHash.Count > 0)
            throw HashCollision(template, Decode(byHash[0].TemplateId));
        var incarnationId = NewIncarnationId();
        context.ExecutableActivityTemplates.Add(ToEntity(template, identity, json, incarnationId));
        context.ExecutableActivityTemplateHashClaims.Add(ToClaimEntity(template, identity, incarnationId));
        return true;
    }

    public async ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var identity = new TemplateIdentity(scope, templateId, null);
        var row = await FindRowByIdAsync(identity, cancellationToken);
        return row is null ? null : Read(row, identity);
    }

    public async ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var identity = new TemplateIdentity(scope, null, templateHash);
        var claim = await FindClaimRowAsync(identity, cancellationToken);
        if (claim is null)
        {
            var orphanedRows = await FindRowsByHashAsync(identity, cancellationToken);
            if (orphanedRows.Count > 0)
                throw new InvalidDataException("Executable activity template exists without its hash claim.");
            return null;
        }
        var readClaim = ReadClaim(claim, identity);
        var templateIdentity = identity with { TemplateId = readClaim.TemplateId };
        var row = await FindRowByIdAsync(templateIdentity, cancellationToken)
                  ?? throw new InvalidDataException("Executable activity template hash claim points to a missing template.");
        EnsureMatchingIncarnation(row, claim);
        EnsureRowHash(row, identity.TemplateHash);
        return Read(row, templateIdentity);
    }

    public async ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var cursor = Decode(request.ContinuationToken, scope);
        var scopeKey = Encode(scope);
        var query = context.ExecutableActivityTemplates.AsNoTracking().Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == scopeKey);
        if (cursor is not null)
            query = query.Where(x => x.TemplateIdOrderKey.CompareTo(cursor.Key) > 0);
        var rows = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "listing",
            scope,
            () => query.OrderBy(x => x.TemplateIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken));
        var hasMore = rows.Length > request.Limit;
        if (hasMore)
            rows = rows[..request.Limit];
        var items = rows.Select(x => Read(x, new TemplateIdentity(scope, Decode(x.TemplateId), x.TemplateHash))).ToArray();
        var next = hasMore ? EncodeCursor(scope, rows[^1].TemplateIdOrderKey) : null;
        return new RuntimeStorePage<ExecutableActivityTemplate>(request, items, next);
    }

    public async ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken = default)
    {
        context.ChangeTracker.Clear();
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var identity = new TemplateIdentity(scope, templateId, null);
        var initialRow = await FindRowByIdAsync(identity, cancellationToken);
        if (initialRow is null)
        {
            context.ChangeTracker.Clear();
            return false;
        }
        var initialTemplate = Read(initialRow, identity);
        var fullIdentity = identity with { TemplateHash = initialTemplate.TemplateHash };
        var initialClaim = await FindClaimRowAsync(fullIdentity, cancellationToken)
                           ?? throw new InvalidDataException("Executable activity template has no hash claim.");
        EnsureOwnedClaim(initialClaim, fullIdentity);
        if (initialClaim.IncarnationId != initialRow.IncarnationId)
            throw new InvalidDataException("Executable activity template and its hash claim have mismatched incarnation identities.");
        var expectedIncarnationId = initialRow.IncarnationId;
        for (var attempt = 0; attempt < MaximumDeleteAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            var row = await FindRowByIdAsync(identity, cancellationToken);
            if (row is null)
            {
                context.ChangeTracker.Clear();
                return false;
            }
            if (row.IncarnationId != expectedIncarnationId)
            {
                context.ChangeTracker.Clear();
                return false;
            }
            var template = Read(row, identity);
            EnsureRowHash(row, fullIdentity.TemplateHash);
            var claim = await FindClaimRowAsync(fullIdentity, cancellationToken)
                        ?? throw new InvalidDataException("Executable activity template has no hash claim.");
            EnsureOwnedClaim(claim, fullIdentity);
            if (claim.IncarnationId != expectedIncarnationId)
            {
                context.ChangeTracker.Clear();
                return false;
            }
            try
            {
                await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                    context, "deleting", templateId, () => context.Database.BeginTransactionAsync(cancellationToken));
                context.Remove(row);
                context.Remove(claim);
                await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                    context, "deleting", templateId, () => context.SaveChangesAsync(cancellationToken));
                await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                    context, "deleting", templateId, () => transaction.CommitAsync(cancellationToken));
                context.ChangeTracker.Clear();
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < MaximumDeleteAttempts)
            {
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsTransientWriteConflict(exception) && attempt + 1 < MaximumDeleteAttempts)
            {
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception)
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("deleting", templateId, exception);
            }
            catch (DbException exception)
            {
                context.ChangeTracker.Clear();
                throw NormalizeProviderFailure("deleting", templateId, exception);
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }
        context.ChangeTracker.Clear();
        throw new InvalidOperationException($"Executable activity template '{templateId}' changed concurrently and did not settle after {MaximumDeleteAttempts} attempts.");
    }

    private async ValueTask ReconcileCreateAsync(ExecutableActivityTemplate template, TemplateIdentity identity, Exception cause, CancellationToken cancellationToken)
    {
        var winnerRow = await FindRowByIdAsync(identity, cancellationToken);
        if (winnerRow is not null)
        {
            var claim = await FindClaimRowAsync(identity, cancellationToken)
                        ?? throw new InvalidDataException("Executable activity template winner has no hash claim.");
            EnsureOwnedClaim(claim, identity);
            EnsureMatchingIncarnation(winnerRow, claim);
            var winner = Read(winnerRow, identity);
            EnsureSameIdentityAndContent(winner, template);
            return;
        }
        var claimRow = await FindClaimRowAsync(identity, cancellationToken);
        if (claimRow is not null)
        {
            var claim = ReadClaim(claimRow, identity);
            if (claim.TemplateId == template.TemplateId)
                throw new InvalidDataException("Executable activity template hash claim exists without its template row.");
            throw HashCollision(template, claim.TemplateId);
        }
        throw new InvalidOperationException("Executable activity template creation failed and no winning row could be reconciled; retry the operation.", cause);
    }

    private async Task<ExecutableActivityTemplateEntity?> FindRowByIdAsync(TemplateIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.TemplateId is null)
            throw new ArgumentException("A template id is required for this lookup.");
        return await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "finding",
            identity.TemplateId,
            () => context.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == CreateId(identity.Scope, identity.TemplateId) && x.ScopeKeyHash == Hash(identity.Scope) && x.ScopeKey == Encode(identity.Scope) &&
                x.TemplateIdHash == Hash(identity.TemplateId) && x.TemplateId == Encode(identity.TemplateId), cancellationToken));
    }

    private async Task<ExecutableActivityTemplateHashClaimEntity?> FindClaimRowAsync(TemplateIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.TemplateHash is null)
            throw new ArgumentException("A template hash is required for this lookup.");
        return await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "finding",
            identity.TemplateHash,
            () => context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == HashClaimId(identity.Scope, identity.TemplateHash) && x.ScopeKeyHash == Hash(identity.Scope) && x.ScopeKey == Encode(identity.Scope) &&
                x.TemplateHashHash == Hash(identity.TemplateHash) && x.TemplateHash == identity.TemplateHash, cancellationToken));
    }

    private async Task<IReadOnlyList<ExecutableActivityTemplateEntity>> FindRowsByHashAsync(TemplateIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.TemplateHash is null)
            throw new ArgumentException("A template hash is required for this lookup.");
        return await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
            context,
            "finding",
            identity.TemplateHash,
            () => context.ExecutableActivityTemplates.AsNoTracking().Where(x =>
                x.ScopeKeyHash == Hash(identity.Scope) && x.ScopeKey == Encode(identity.Scope) &&
                x.TemplateHashHash == Hash(identity.TemplateHash) && x.TemplateHash == identity.TemplateHash)
                .OrderBy(x => x.TemplateIdOrderKey).ThenBy(x => x.Id).Take(2).ToArrayAsync(cancellationToken));
    }

    // Admits privileged maintenance of one partition, as every runtime artifact store does.
    private string RequireScope() => accessContextAccessor.Current.RequireScope(admitPrivileged: true).Value;

    private static ExecutableActivityTemplateEntity ToEntity(ExecutableActivityTemplate template, TemplateIdentity identity, string json, string incarnationId) => new()
    {
        Id = CreateId(identity.Scope, template.TemplateId), ScopeKey = Encode(identity.Scope), ScopeKeyHash = Hash(identity.Scope), TemplateId = Encode(template.TemplateId),
        TemplateIdHash = Hash(template.TemplateId), TemplateHash = template.TemplateHash, TemplateHashHash = Hash(template.TemplateHash), TemplateIdOrderKey = OrderKey(template.TemplateId), ContentJson = json,
        SchemaVersion = RuntimeArtifactEfModule.SchemaVersion, Revision = 1, IncarnationId = incarnationId
    };

    private static ExecutableActivityTemplateHashClaimEntity ToClaimEntity(ExecutableActivityTemplate template, TemplateIdentity identity, string incarnationId) => new()
    {
        Id = HashClaimId(identity.Scope, template.TemplateHash), ScopeKey = Encode(identity.Scope), ScopeKeyHash = Hash(identity.Scope), TemplateHash = template.TemplateHash,
        TemplateHashHash = Hash(template.TemplateHash), TemplateId = Encode(template.TemplateId), ContentJson = RuntimeArtifactJson.Serialize(new HashClaim(template.TemplateHash, template.TemplateId)),
        SchemaVersion = RuntimeArtifactEfModule.SchemaVersion, Revision = 1, IncarnationId = incarnationId
    };

    private static ExecutableActivityTemplate Read(ExecutableActivityTemplateEntity row, TemplateIdentity identity)
    {
        if (identity.TemplateId is null || row.Id != CreateId(identity.Scope, identity.TemplateId) || row.ScopeKey != Encode(identity.Scope) || row.ScopeKeyHash != Hash(identity.Scope) ||
            row.TemplateId != Encode(identity.TemplateId) || row.TemplateIdHash != Hash(identity.TemplateId) || row.TemplateHashHash != Hash(row.TemplateHash) || row.TemplateIdOrderKey != OrderKey(identity.TemplateId) || row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.Revision <= 0 || string.IsNullOrWhiteSpace(row.IncarnationId))
            throw new InvalidDataException("The persisted executable activity template row is corrupt.");
        try
        {
            var envelope = JsonNode.Parse(row.ContentJson)?.AsObject() ?? throw new InvalidDataException("The persisted executable activity template envelope is empty.");
            if (!StringComparer.Ordinal.Equals(ReadString(envelope, "collection"), "executableActivityTemplate") || !StringComparer.Ordinal.Equals(Decode(ReadString(envelope, "templateHash")), row.TemplateHash) || envelope["template"] is null)
                throw new InvalidDataException("The persisted executable activity template envelope projection is corrupt.");
            var value = RuntimeArtifactJson.Deserialize<ExecutableActivityTemplate>(envelope["template"]!.ToJsonString());
            Validate(value);
            if (value.TemplateId != Decode(row.TemplateId) || value.TemplateHash != row.TemplateHash)
                throw new InvalidDataException("The persisted executable activity template projection is corrupt.");
            return value;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException or OverflowException)
        { throw new InvalidDataException("The persisted executable activity template payload is corrupt.", exception); }
    }

    private static HashClaim ReadClaim(ExecutableActivityTemplateHashClaimEntity row, TemplateIdentity identity)
    {
        if (identity.TemplateHash is null || row.Id != HashClaimId(identity.Scope, identity.TemplateHash) || row.ScopeKey != Encode(identity.Scope) || row.ScopeKeyHash != Hash(identity.Scope) ||
            row.TemplateHash != identity.TemplateHash || row.TemplateHashHash != Hash(identity.TemplateHash) || string.IsNullOrWhiteSpace(row.TemplateId) || row.TemplateId.Length > RuntimeArtifactEfModule.IdentityProjectionMaximumLength || row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.Revision <= 0 || string.IsNullOrWhiteSpace(row.IncarnationId))
            throw new InvalidDataException("The persisted executable activity template hash claim is corrupt.");
        try
        {
            var claim = RuntimeArtifactJson.Deserialize<HashClaim>(row.ContentJson);
            if (claim.TemplateHash != identity.TemplateHash || claim.TemplateId != Decode(row.TemplateId))
                throw new InvalidDataException("The persisted executable activity template hash claim projection is corrupt.");
            return claim;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException or OverflowException)
        { throw new InvalidDataException("The persisted executable activity template hash claim payload is corrupt.", exception); }
    }

    private static void EnsureOwnedClaim(ExecutableActivityTemplateHashClaimEntity? row, TemplateIdentity identity)
    {
        if (row is null)
            throw new InvalidDataException("Executable activity template has no hash claim.");
        if (identity.TemplateHash is null || ReadClaim(row, identity).TemplateId != identity.TemplateId)
            throw new InvalidOperationException($"Executable activity template hash '{identity.TemplateHash}' is owned by another template.");
    }

    private static void EnsureMatchingIncarnation(ExecutableActivityTemplateEntity template, ExecutableActivityTemplateHashClaimEntity claim)
    {
        if (!StringComparer.Ordinal.Equals(template.IncarnationId, claim.IncarnationId))
            throw new InvalidDataException("Executable activity template and its hash claim have mismatched incarnation identities.");
    }

    private static void EnsureRowHash(ExecutableActivityTemplateEntity row, string? expectedHash)
    {
        if (expectedHash is null || !StringComparer.Ordinal.Equals(row.TemplateHash, expectedHash) || !StringComparer.Ordinal.Equals(row.TemplateHashHash, Hash(expectedHash)))
            throw new InvalidDataException("Executable activity template row hash does not match its requested hash claim.");
    }

    private static void EnsureSameIdentityAndContent(ExecutableActivityTemplate existing, ExecutableActivityTemplate candidate)
    {
        if (existing.TemplateHash != candidate.TemplateHash || !JsonNode.DeepEquals(ComparableContent(existing), ComparableContent(candidate)))
            throw new InvalidOperationException($"Template id '{candidate.TemplateId}' and hash '{candidate.TemplateHash}' are already bound to different content.");
    }

    private static JsonObject ComparableContent(ExecutableActivityTemplate template)
    {
        var objectNode = JsonNode.Parse(RuntimeArtifactJson.Serialize(template))?.AsObject() ?? throw new InvalidDataException("Executable activity template content could not be compared.");
        objectNode.Remove("createdAt");
        objectNode.Remove("nodesById");
        return objectNode;
    }

    private static string SerializeEnvelope(ExecutableActivityTemplate template)
    {
        var payload = JsonNode.Parse(RuntimeArtifactJson.Serialize(template)) ?? throw new InvalidDataException("Executable activity template payload could not be serialized.");
        payload.AsObject().Remove("nodesById");
        return new JsonObject { ["collection"] = "executableActivityTemplate", ["templateHash"] = Encode(template.TemplateHash), ["template"] = payload }.ToJsonString();
    }

    private string EncodeCursor(string scope, string key) => continuationCodec.Encode(ContinuationPurpose, Encoding.UTF8.GetBytes(RuntimeArtifactJson.Serialize(new Cursor(1, Hash(scope), key))));

    private Cursor? Decode(string? token, string scope)
    {
        if (token is null)
            return null;
        try
        {
            var cursor = RuntimeArtifactJson.Deserialize<Cursor>(Encoding.UTF8.GetString(continuationCodec.Decode(ContinuationPurpose, token)));
            if (cursor.Version != 1 || cursor.ScopeHash != Hash(scope) || string.IsNullOrWhiteSpace(cursor.Key))
                throw new FormatException();
            return cursor;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException)
        { throw new ArgumentException("The executable activity template continuation token is invalid.", nameof(token), exception); }
    }

    private static string ReadString(JsonObject node, string property) => node[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : throw new InvalidDataException($"The executable activity template envelope is missing '{property}'.");
    private static InvalidOperationException HashCollision(ExecutableActivityTemplate template, string owner) => new($"Template hash '{template.TemplateHash}' is already bound to id '{owner}', not '{template.TemplateId}'.");
    private static void Validate(ExecutableActivityTemplate value) { ArgumentNullException.ThrowIfNull(value); ArgumentException.ThrowIfNullOrWhiteSpace(value.TemplateId); ArgumentException.ThrowIfNullOrWhiteSpace(value.TemplateHash); if (value.TemplateId.Length > RuntimeArtifactEfModule.IdentityMaximumLength) throw new ArgumentOutOfRangeException(nameof(value.TemplateId)); if (value.TemplateHash.Length > RuntimeArtifactEfModule.HashMaximumLength) throw new ArgumentOutOfRangeException(nameof(value.TemplateHash)); }
    private static string CreateId(string scope, string value) => Hash($"{scope.Length}:{scope}{value.Length}:{value}");
    private static string NewIncarnationId() => Guid.NewGuid().ToString("N");
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Decode(string value) => EfRelationalIdentity.Decode(value);

    private static bool NamesTemplate(string? encodedTemplateId, ExecutableActivityTemplate template) =>
        encodedTemplateId is not null && StringComparer.Ordinal.Equals(Decode(encodedTemplateId), template.TemplateId);
    private static string HashClaimId(string scope, string hash) => CreateId(scope, $"templateHash:{Hash(hash)}");
    private static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeArtifactEfModule.IdentityMaximumLength));
    private static RuntimeArtifactEntityFrameworkPersistenceException NormalizeProviderFailure(string operation, string identity, Exception inner) =>
        new(operation, identity, $"The EF runtime artifact store failed while {operation} executable activity template '{identity}'.", inner);
    private sealed record TemplateIdentity(string Scope, string? TemplateId, string? TemplateHash);
    private sealed record HashClaim(string TemplateHash, string TemplateId);
    private sealed record Cursor(int Version, string ScopeHash, string Key);
}
