using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

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
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                var current = await FindRowByIdAsync(identity, cancellationToken);
                var claim = await FindClaimRowAsync(identity, cancellationToken);
                var byHash = await FindRowsByHashAsync(identity, cancellationToken);
                if (current is not null)
                {
                    var existing = Read(current, identity);
                    EnsureOwnedClaim(claim, identity);
                    EnsureSameIdentityAndContent(existing, template);
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
                if (claim is not null)
                {
                    var existingClaim = ReadClaim(claim, identity);
                    if (existingClaim.TemplateId == template.TemplateId)
                        throw new InvalidDataException("Executable activity template hash claim exists without its template row.");
                    throw HashCollision(template, existingClaim.TemplateId);
                }
                if (byHash.Count > 0)
                    throw HashCollision(template, byHash[0].TemplateId);
                context.ExecutableActivityTemplates.Add(ToEntity(template, identity, json));
                context.ExecutableActivityTemplateHashClaims.Add(ToClaimEntity(template, identity));
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) || EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
                if (attempt + 1 == MaximumCreateAttempts)
                    await ReconcileCreateAsync(template, identity, exception, cancellationToken);
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        }
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
        var rows = await query.OrderBy(x => x.TemplateIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > request.Limit;
        if (hasMore)
            rows = rows[..request.Limit];
        var items = rows.Select(x => Read(x, new TemplateIdentity(scope, x.TemplateId, x.TemplateHash))).ToArray();
        var next = hasMore ? EncodeCursor(scope, rows[^1].TemplateIdOrderKey) : null;
        return new RuntimeStorePage<ExecutableActivityTemplate>(request, items, next);
    }

    public async ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var identity = new TemplateIdentity(scope, templateId, null);
        for (var attempt = 0; attempt < MaximumDeleteAttempts; attempt++)
        {
            context.ChangeTracker.Clear();
            var row = await FindRowByIdAsync(identity, cancellationToken);
            if (row is null)
                return false;
            var template = Read(row, identity);
            var fullIdentity = identity with { TemplateHash = template.TemplateHash };
            var claim = await FindClaimRowAsync(fullIdentity, cancellationToken)
                        ?? throw new InvalidDataException("Executable activity template has no hash claim.");
            EnsureOwnedClaim(claim, fullIdentity);
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                context.Remove(row);
                context.Remove(claim);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt + 1 < MaximumDeleteAttempts)
            {
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateException) when (attempt + 1 < MaximumDeleteAttempts)
            {
                context.ChangeTracker.Clear();
            }
        }
        throw new InvalidOperationException($"Executable activity template '{templateId}' changed concurrently and did not settle after {MaximumDeleteAttempts} attempts.");
    }

    private async ValueTask ReconcileCreateAsync(ExecutableActivityTemplate template, TemplateIdentity identity, Exception cause, CancellationToken cancellationToken)
    {
        var winner = await FindAsync(template.TemplateId, cancellationToken);
        if (winner is not null)
        {
            EnsureSameIdentityAndContent(winner, template);
            var claim = await FindClaimRowAsync(identity, cancellationToken)
                        ?? throw new InvalidDataException("Executable activity template winner has no hash claim.");
            EnsureOwnedClaim(claim, identity);
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
        return await context.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == CreateId(identity.Scope, identity.TemplateId) && x.ScopeKeyHash == Hash(identity.Scope) && x.ScopeKey == Encode(identity.Scope) &&
            x.TemplateIdHash == Hash(identity.TemplateId) && x.TemplateId == identity.TemplateId, cancellationToken);
    }

    private async Task<ExecutableActivityTemplateHashClaimEntity?> FindClaimRowAsync(TemplateIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.TemplateHash is null)
            throw new ArgumentException("A template hash is required for this lookup.");
        return await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == HashClaimId(identity.Scope, identity.TemplateHash) && x.ScopeKeyHash == Hash(identity.Scope) && x.ScopeKey == Encode(identity.Scope) &&
            x.TemplateHashHash == Hash(identity.TemplateHash) && x.TemplateHash == identity.TemplateHash, cancellationToken);
    }

    private async Task<IReadOnlyList<ExecutableActivityTemplateEntity>> FindRowsByHashAsync(TemplateIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.TemplateHash is null)
            throw new ArgumentException("A template hash is required for this lookup.");
        return await context.ExecutableActivityTemplates.AsNoTracking().Where(x =>
            x.ScopeKeyHash == Hash(identity.Scope) && x.ScopeKey == Encode(identity.Scope) &&
            x.TemplateHash == identity.TemplateHash)
            .OrderBy(x => x.TemplateId).Take(2).ToArrayAsync(cancellationToken);
    }

    private string RequireScope()
    {
        var current = accessContextAccessor.Current;
        if (current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF executable activity template persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    private static ExecutableActivityTemplateEntity ToEntity(ExecutableActivityTemplate template, TemplateIdentity identity, string json) => new()
    {
        Id = CreateId(identity.Scope, template.TemplateId), ScopeKey = Encode(identity.Scope), ScopeKeyHash = Hash(identity.Scope), TemplateId = template.TemplateId,
        TemplateIdHash = Hash(template.TemplateId), TemplateHash = template.TemplateHash, TemplateIdOrderKey = OrderKey(template.TemplateId), ContentJson = json,
        SchemaVersion = RuntimeArtifactEfModule.SchemaVersion, Revision = 1
    };

    private static ExecutableActivityTemplateHashClaimEntity ToClaimEntity(ExecutableActivityTemplate template, TemplateIdentity identity) => new()
    {
        Id = HashClaimId(identity.Scope, template.TemplateHash), ScopeKey = Encode(identity.Scope), ScopeKeyHash = Hash(identity.Scope), TemplateHash = template.TemplateHash,
        TemplateHashHash = Hash(template.TemplateHash), TemplateId = template.TemplateId, ContentJson = RuntimeArtifactJson.Serialize(new HashClaim(template.TemplateHash, template.TemplateId)),
        SchemaVersion = RuntimeArtifactEfModule.SchemaVersion, Revision = 1
    };

    private static ExecutableActivityTemplate Read(ExecutableActivityTemplateEntity row, TemplateIdentity identity)
    {
        if (identity.TemplateId is null || row.Id != CreateId(identity.Scope, identity.TemplateId) || row.ScopeKey != Encode(identity.Scope) || row.ScopeKeyHash != Hash(identity.Scope) ||
            row.TemplateId != identity.TemplateId || row.TemplateIdHash != Hash(identity.TemplateId) || row.TemplateIdOrderKey != OrderKey(identity.TemplateId) || row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.Revision <= 0)
            throw new InvalidDataException("The persisted executable activity template row is corrupt.");
        try
        {
            var envelope = JsonNode.Parse(row.ContentJson)?.AsObject() ?? throw new InvalidDataException("The persisted executable activity template envelope is empty.");
            if (!StringComparer.Ordinal.Equals(ReadString(envelope, "collection"), "executableActivityTemplate") || !StringComparer.Ordinal.Equals(ReadString(envelope, "templateHash"), row.TemplateHash) || envelope["template"] is null)
                throw new InvalidDataException("The persisted executable activity template envelope projection is corrupt.");
            var value = RuntimeArtifactJson.Deserialize<ExecutableActivityTemplate>(envelope["template"]!.ToJsonString());
            Validate(value);
            if (value.TemplateId != row.TemplateId || value.TemplateHash != row.TemplateHash)
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
            row.TemplateHash != identity.TemplateHash || row.TemplateHashHash != Hash(identity.TemplateHash) || string.IsNullOrWhiteSpace(row.TemplateId) || row.TemplateId.Length > RuntimeArtifactEfModule.IdentityMaximumLength || row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion || row.Revision <= 0)
            throw new InvalidDataException("The persisted executable activity template hash claim is corrupt.");
        try
        {
            var claim = RuntimeArtifactJson.Deserialize<HashClaim>(row.ContentJson);
            if (claim.TemplateHash != identity.TemplateHash || claim.TemplateId != row.TemplateId)
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
        return new JsonObject { ["collection"] = "executableActivityTemplate", ["templateHash"] = template.TemplateHash, ["template"] = payload }.ToJsonString();
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
    private static void Validate(ExecutableActivityTemplate value) { ArgumentNullException.ThrowIfNull(value); ArgumentException.ThrowIfNullOrWhiteSpace(value.TemplateId); ArgumentException.ThrowIfNullOrWhiteSpace(value.TemplateHash); if (value.TemplateId.Length > RuntimeArtifactEfModule.IdentityMaximumLength) throw new ArgumentOutOfRangeException(nameof(value.TemplateId)); if (value.TemplateHash.Length > 450) throw new ArgumentOutOfRangeException(nameof(value.TemplateHash)); }
    private static string CreateId(string scope, string value) => Hash($"{scope.Length}:{scope}{value.Length}:{value}");
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string HashClaimId(string scope, string hash) => CreateId(scope, $"templateHash:{Hash(hash)}");
    private static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeArtifactEfModule.IdentityMaximumLength));
    private sealed record TemplateIdentity(string Scope, string? TemplateId, string? TemplateHash);
    private sealed record HashClaim(string TemplateHash, string TemplateId);
    private sealed record Cursor(int Version, string ScopeHash, string Key);
}
