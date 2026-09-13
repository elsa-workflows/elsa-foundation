using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed class EfExecutableActivityTemplateStore(
    BookmarkStateDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IExecutableActivityTemplateStore
{
    public async ValueTask SaveAsync(ExecutableActivityTemplate template, CancellationToken cancellationToken = default)
    {
        Validate(template);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        context.ChangeTracker.Clear();
        var id = CreateId(scope, template.TemplateId);
        var claimId = HashClaimId(scope, template.TemplateHash);
        var json = RuntimeArtifactJson.Serialize(template);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var current = await context.ExecutableActivityTemplates.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.TemplateIdHash == Hash(template.TemplateId) && x.TemplateId == template.TemplateId, cancellationToken);
            var claim = await context.ExecutableActivityTemplateHashClaims.SingleOrDefaultAsync(x => x.Id == claimId && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.TemplateHashHash == Hash(template.TemplateHash) && x.TemplateHash == template.TemplateHash, cancellationToken);
            if (current is not null)
            {
                var existing = Read(current, scope, template.TemplateId, id);
                if (claim is null)
                    throw new InvalidDataException("Executable activity template has no hash claim.");
                EnsureClaim(claim, scope, template.TemplateHash, template.TemplateId, claimId);
                if (RuntimeArtifactJson.Serialize(existing) != json)
                    throw new InvalidOperationException($"Executable activity template '{template.TemplateId}' already exists with different content.");
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (claim is not null)
            {
                EnsureClaim(claim, scope, template.TemplateHash, template.TemplateId, claimId);
                throw new InvalidOperationException($"Executable activity template hash '{template.TemplateHash}' is owned by another template.");
            }
            context.ExecutableActivityTemplates.Add(new ExecutableActivityTemplateEntity { Id = id, ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope), TemplateId = template.TemplateId, TemplateIdHash = Hash(template.TemplateId), TemplateHash = template.TemplateHash, TemplateIdOrderKey = OrderKey(template.TemplateId), ContentJson = json, SchemaVersion = RuntimeArtifactEfModule.SchemaVersion });
            context.ExecutableActivityTemplateHashClaims.Add(new ExecutableActivityTemplateHashClaimEntity { Id = claimId, ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope), TemplateHash = template.TemplateHash, TemplateHashHash = Hash(template.TemplateHash), TemplateId = template.TemplateId, ContentJson = RuntimeArtifactJson.Serialize(new HashClaim(template.TemplateHash, template.TemplateId)), SchemaVersion = RuntimeArtifactEfModule.SchemaVersion });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) || EfRelationalExceptionClassifier.IsTransientWriteConflict(exception)) { context.ChangeTracker.Clear(); throw new InvalidOperationException("Executable activity template hash ownership changed concurrently; retry the operation.", exception); }
        catch { context.ChangeTracker.Clear(); throw; }
    }

    public async ValueTask<ExecutableActivityTemplate?> FindAsync(string templateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, templateId);
        var row = await context.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.TemplateIdHash == Hash(templateId) && x.TemplateId == templateId, cancellationToken);
        return row is null ? null : Read(row, scope, templateId, id);
    }

    public async ValueTask<ExecutableActivityTemplate?> FindByHashAsync(string templateHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var claimId = HashClaimId(scope, templateHash);
        var claim = await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claimId && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.TemplateHashHash == Hash(templateHash) && x.TemplateHash == templateHash, cancellationToken);
        if (claim is null)
            return null;
        var c = ReadClaim(claim, scope, templateHash, claimId);
        var id = CreateId(scope, c.TemplateId);
        var row = await context.ExecutableActivityTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.TemplateIdHash == Hash(c.TemplateId) && x.TemplateId == c.TemplateId, cancellationToken) ?? throw new InvalidDataException("Executable activity template hash claim points to a missing template.");
        return Read(row, scope, c.TemplateId, id);
    }

    public async ValueTask<RuntimeStorePage<ExecutableActivityTemplate>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var cursor = Decode(request.ContinuationToken, scope);
        var rows = await context.ExecutableActivityTemplates.AsNoTracking().Where(x => x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && (cursor == null || string.CompareOrdinal(x.TemplateIdOrderKey, cursor) > 0)).OrderBy(x => x.TemplateIdOrderKey).Take(request.Limit + 1).ToArrayAsync(cancellationToken);
        var more = rows.Length > request.Limit;
        if (more)
            rows = rows[..request.Limit];
        return new RuntimeStorePage<ExecutableActivityTemplate>(request, rows.Select(x => Read(x, scope, x.TemplateId, x.Id)).ToArray(), more ? Encode(rows[^1].TemplateIdOrderKey, scope) : null);
    }

    public async ValueTask<bool> DeleteAsync(string templateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope();
        var id = CreateId(scope, templateId);
        var row = await context.ExecutableActivityTemplates.SingleOrDefaultAsync(x => x.Id == id && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.TemplateIdHash == Hash(templateId) && x.TemplateId == templateId, cancellationToken);
        if (row is null)
            return false;
        var template = Read(row, scope, templateId, id);
        var claimId = HashClaimId(scope, template.TemplateHash);
        var claim = await context.ExecutableActivityTemplateHashClaims.SingleOrDefaultAsync(x => x.Id == claimId && x.ScopeKeyHash == Hash(scope) && x.ScopeKey == Encode(scope) && x.TemplateHashHash == Hash(template.TemplateHash) && x.TemplateHash == template.TemplateHash, cancellationToken) ?? throw new InvalidDataException("Executable activity template has no hash claim.");
        EnsureClaim(claim, scope, template.TemplateHash, templateId, claimId);
        context.RemoveRange(row, claim);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private string RequireScope()
    {
        var current = accessContextAccessor.Current;
        if (current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("EF executable activity template persistence requires one explicit persistence scope.");
        return current.Scope.Value;
    }

    private static ExecutableActivityTemplate Read(ExecutableActivityTemplateEntity row, string scope, string expected, string id)
    {
        if (row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || row.TemplateId != expected || row.TemplateIdHash != Hash(expected) || row.TemplateIdOrderKey != OrderKey(expected) || row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion)
            throw new InvalidDataException("The persisted executable activity template row is corrupt.");
        try
        { var value = RuntimeArtifactJson.Deserialize<ExecutableActivityTemplate>(row.ContentJson); if (value.TemplateId != expected || value.TemplateHash != row.TemplateHash) throw new InvalidDataException("The persisted executable activity template projection is corrupt."); return value; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException) { throw new InvalidDataException("The persisted executable activity template payload is corrupt.", exception); }
    }

    private static HashClaim ReadClaim(ExecutableActivityTemplateHashClaimEntity row, string scope, string hash, string id)
    {
        if (row.Id != id || row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) || row.TemplateHash != hash || row.TemplateHashHash != Hash(hash) || row.SchemaVersion != RuntimeArtifactEfModule.SchemaVersion)
            throw new InvalidDataException("The persisted executable activity template hash claim is corrupt.");
        var claim = RuntimeArtifactJson.Deserialize<HashClaim>(row.ContentJson);
        if (claim.TemplateHash != hash)
            throw new InvalidDataException("The persisted executable activity template hash claim payload is corrupt.");
        return claim;
    }

    private static void EnsureClaim(ExecutableActivityTemplateHashClaimEntity row, string scope, string hash, string templateId, string claimId)
    { if (ReadClaim(row, scope, hash, claimId).TemplateId != templateId) throw new InvalidOperationException($"Executable activity template hash '{hash}' is owned by another template."); }
    private static void Validate(ExecutableActivityTemplate value) { ArgumentNullException.ThrowIfNull(value); ArgumentException.ThrowIfNullOrWhiteSpace(value.TemplateId); ArgumentException.ThrowIfNullOrWhiteSpace(value.TemplateHash); if (value.TemplateId.Length > RuntimeArtifactEfModule.IdentityMaximumLength) throw new ArgumentOutOfRangeException(nameof(value.TemplateId)); if (value.TemplateHash.Length > 450) throw new ArgumentOutOfRangeException(nameof(value.TemplateHash)); }
    private static string CreateId(string scope, string value) => Hash($"{scope.Length}:{scope}{value.Length}:{value}");
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string HashClaimId(string scope, string hash) => CreateId(scope, $"templateHash:{Hash(hash)}");
    private static string OrderKey(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeArtifactEfModule.IdentityMaximumLength));
    private static string Encode(string value, string scope) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Hash(scope)}:{value}"));
    private static string? Decode(string? value, string scope) { if (value is null) return null; try { var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)); var prefix = $"{Hash(scope)}:"; if (!decoded.StartsWith(prefix, StringComparison.Ordinal)) throw new FormatException(); return decoded[prefix.Length..]; } catch (Exception exception) when (exception is FormatException or ArgumentException) { throw new ArgumentException("The executable activity template continuation token is invalid.", nameof(value), exception); } }
    private sealed record HashClaim(string TemplateHash, string TemplateId);
}
