using System.Buffers.Binary;
using System.Security.Cryptography;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

internal sealed class EfAuthenticationArtifactStore(ExternalAuthenticationDbContext context, IPersistenceAccessContextAccessor access) : IAuthenticationArtifactStore
{
    public bool IsDurable => true;

    public async ValueTask<bool> TryCreateAsync(AuthenticationArtifact artifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        Prepare(artifact.TenantId, artifact.Id, artifact.Purpose, artifact.BrowserBinding, artifact.AdministratorBinding, cancellationToken);
        if (string.IsNullOrWhiteSpace(artifact.ProtectedPayload) || artifact.ProtectedPayload.Length > 196608 ||
            artifact.ExpiresAt <= artifact.CreatedAt || artifact.ExpiresAt - artifact.CreatedAt > TimeSpan.FromMinutes(20))
            throw new ArgumentException("Invalid or oversized authentication artifact.", nameof(artifact));
        try
        {
            await Cleanup(artifact.TenantId, artifact.CreatedAt, cancellationToken);
            var entity = new AuthenticationArtifactEntity
            {
                Id = artifact.Id, TenantKey = TenantKey(artifact.TenantId), Purpose = (int)artifact.Purpose,
                BrowserBinding = artifact.BrowserBinding, AdministratorBinding = artifact.AdministratorBinding,
                CreatedAtMilliseconds = artifact.CreatedAt.ToUnixTimeMilliseconds(), ExpiresAtMilliseconds = artifact.ExpiresAt.ToUnixTimeMilliseconds(),
                ProtectedPayload = artifact.ProtectedPayload, Version = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                SchemaVersion = ExternalAuthenticationEfModule.SchemaVersion
            };
            context.Artifacts.Add(entity);
            try { await context.SaveChangesAsync(cancellationToken); return true; }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            { context.ChangeTracker.Clear(); return false; }
            finally { context.Entry(entity).State = EntityState.Detached; }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not EfSchemaVersionSkewException and not AuthenticationStoreException)
        {
            throw new AuthenticationStoreException("create-artifact", artifact.TenantId, artifact.Id, exception);
        }
    }

    public async ValueTask<AuthenticationArtifact?> TryConsumeAsync(string tenantId, string id, AuthenticationArtifactPurpose purpose,
        string browserBinding, string? administratorBinding, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Prepare(tenantId, id, purpose, browserBinding, administratorBinding, cancellationToken);
        try
        {
            var tenantKey = TenantKey(tenantId);
            var milliseconds = now.ToUnixTimeMilliseconds();
            var query = context.Artifacts.AsNoTracking().Where(row => row.Id == id && row.TenantKey == tenantKey && row.Purpose == (int)purpose &&
                row.BrowserBinding == browserBinding && row.AdministratorBinding == administratorBinding && row.ExpiresAtMilliseconds > milliseconds);
            var entity = await query.SingleOrDefaultAsync(cancellationToken);
            if (entity is null) return null;
            EfSchemaVersion.EnsureReadable(ExternalAuthenticationEfModule.Chain, entity.SchemaVersion);
            // A concurrent consumer can read the same row, but only one conditional deletion acknowledges it.
            // On an uncertain provider failure, throw and issue no identity/session; never replay a consume receipt.
            if (await query.Where(row => row.Version == entity.Version).ExecuteDeleteAsync(cancellationToken) != 1) return null;
            return new(entity.Id, tenantId, purpose, entity.BrowserBinding, entity.AdministratorBinding,
                DateTimeOffset.FromUnixTimeMilliseconds(entity.CreatedAtMilliseconds), DateTimeOffset.FromUnixTimeMilliseconds(entity.ExpiresAtMilliseconds), entity.ProtectedPayload);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not EfSchemaVersionSkewException and not AuthenticationStoreException)
        {
            throw new AuthenticationStoreException("consume-artifact", tenantId, id, exception);
        }
    }

    private async Task Cleanup(string tenantId, DateTimeOffset now, CancellationToken token)
    {
        var tenantKey = TenantKey(tenantId);
        var milliseconds = now.ToUnixTimeMilliseconds();
        var expired = await context.Artifacts.AsNoTracking().Where(row => row.TenantKey == tenantKey && row.ExpiresAtMilliseconds <= milliseconds && row.SchemaVersion == ExternalAuthenticationEfModule.SchemaVersion)
            .OrderBy(row => row.ExpiresAtMilliseconds).ThenBy(row => row.Id).Select(row => row.Id).Take(64).ToArrayAsync(token);
        if (expired.Length > 0) await context.Artifacts.Where(row => row.TenantKey == tenantKey && expired.Contains(row.Id) &&
            row.ExpiresAtMilliseconds <= milliseconds && row.SchemaVersion == ExternalAuthenticationEfModule.SchemaVersion).ExecuteDeleteAsync(token);
    }

    private void Prepare(string tenantId, string id, AuthenticationArtifactPurpose purpose, string browser, string? administrator, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (tenantId.Length > 400 || !Hash(id) || !Hash(browser) || !Enum.IsDefined(purpose) ||
            (purpose == AuthenticationArtifactPurpose.SignIn ? administrator is not null : !Hash(administrator)))
            throw new ArgumentException("Invalid authentication artifact scope.");
        access.Current.EnsureScope(new PersistenceScope(tenantId));
        context.EnsureProviderBinding();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("One-time authentication artifacts require an independently committed operation.");
        token.ThrowIfCancellationRequested();
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string TenantKey(string tenant)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> unit = stackalloc byte[sizeof(char)];
        foreach (var character in tenant) { BinaryPrimitives.WriteUInt16BigEndian(unit, character); hash.AppendData(unit); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
