using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

internal sealed class EfAuthenticationSessionStore(ExternalAuthenticationDbContext context,
    IPersistenceAccessContextAccessor access, IDataProtectionProvider protection) : IAuthenticationSessionStore
{
    public bool IsDurable => true;

    public async ValueTask<bool> TryCreateAsync(AuthenticationSessionRecord session, CancellationToken cancellationToken = default)
    {
        Prepare(session.TenantId, session.Id, cancellationToken);
        var credential = session.ConnectionMaterialRevision is not null || session.ConnectionCredentialBinding is not null || session.ConnectionRedirectUri is not null;
        if (!Text(session.UserId, 400) || !Text(session.Provider, 400) || !Text(session.SecurityVersion, 400) ||
            session.CreatedAt.Offset != TimeSpan.Zero || session.ExpiresAt.Offset != TimeSpan.Zero ||
            session.CreatedAt.Ticks % TimeSpan.TicksPerMillisecond != 0 || session.ExpiresAt.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            session.ExpiresAt <= session.CreatedAt || session.ExpiresAt - session.CreatedAt > TimeSpan.FromDays(30) ||
            session.RevokedAt is not null || session.Revision is not null ||
            credential && (session.ConnectionKey is null || !Text(session.ConnectionMaterialRevision, 128) ||
                !Text(session.ConnectionCredentialBinding, 128) || !Text(session.ConnectionRedirectUri, 4096)) ||
            (session.ConnectionKey is null ? session.ConnectionRecordId is not null || session.Issuer is not null || session.ExternalSubject is not null || session.ExternalLinkRevision is not null :
                !Text(session.ConnectionKey, 80) || !Text(session.ConnectionRecordId, 400) || !Text(session.Issuer, 400) || !Text(session.ExternalSubject, 400) || !Text(session.ExternalLinkRevision, 128)))
            throw new ArgumentException("Invalid authentication session.", nameof(session));
        try
        {
            var json = JsonSerializer.Serialize(session);
            if (Encoding.UTF8.GetByteCount(json) > 16384) throw new ArgumentException("Oversized authentication session.", nameof(session));
            var entity = new AuthenticationSessionEntity
            {
                TenantKey = Hash(session.TenantId), Id = session.Id, UserKey = Hash(session.UserId),
                CreatedAtMilliseconds = session.CreatedAt.ToUnixTimeMilliseconds(), ExpiresAtMilliseconds = session.ExpiresAt.ToUnixTimeMilliseconds(),
                Version = NewVersion(), SchemaVersion = ExternalAuthenticationEfModule.SchemaVersion
            };
            entity.ProtectedPayload = Protector(entity).Protect(json);
            await Cleanup(entity.TenantKey, entity.CreatedAtMilliseconds, cancellationToken);
            context.Sessions.Add(entity);
            try { await context.SaveChangesAsync(cancellationToken); return true; }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            { context.ChangeTracker.Clear(); return false; }
            finally { context.Entry(entity).State = EntityState.Detached; }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not EfSchemaVersionSkewException and not AuthenticationStoreException)
        {
            throw new AuthenticationStoreException("create-session", session.TenantId, session.Id, exception);
        }
    }

    public async ValueTask<AuthenticationSessionRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        Prepare(tenantId, id, cancellationToken);
        try
        {
            var tenant = Hash(tenantId);
            var entity = await context.Sessions.AsNoTracking().SingleOrDefaultAsync(row => row.TenantKey == tenant && row.Id == id, cancellationToken);
            return entity is null ? null : Read(entity, tenantId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not EfSchemaVersionSkewException and not AuthenticationStoreException)
        {
            throw new AuthenticationStoreException("find-session", tenantId, id, exception);
        }
    }

    public async ValueTask<IamPage<AuthenticationSessionRecord>> ListAsync(string tenantId, string userId, IamPageRequest page, CancellationToken cancellationToken = default)
    {
        Prepare(tenantId, null, cancellationToken);
        if (!Text(userId, 400) || page.Skip < 0 || page.Take is < 1 or > 100) throw new ArgumentException("Invalid authentication session page.");
        try
        {
            var tenant = Hash(tenantId); var user = Hash(userId);
            var query = context.Sessions.AsNoTracking().Where(row => row.TenantKey == tenant && row.UserKey == user);
            var count = await query.CountAsync(cancellationToken);
            var entities = await query.OrderByDescending(row => row.CreatedAtMilliseconds).ThenBy(row => row.Id).Skip(page.Skip).Take(page.Take).ToArrayAsync(cancellationToken);
            var records = entities.Select(row => Read(row, tenantId)).ToArray();
            if (records.Any(record => record.UserId != userId)) throw new InvalidOperationException("Invalid authentication session owner.");
            return new(records, count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not EfSchemaVersionSkewException and not AuthenticationStoreException)
        {
            throw new AuthenticationStoreException("list-sessions", tenantId, null, exception);
        }
    }

    public async ValueTask<AuthenticationSessionRevocationStatus> RevokeAsync(string tenantId, string id, string expectedRevision,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Prepare(tenantId, id, cancellationToken);
        if (!Hex(expectedRevision)) throw new ArgumentException("A session revision is required.");
        try
        {
            var tenant = Hash(tenantId);
            var entity = await context.Sessions.AsNoTracking().SingleOrDefaultAsync(row => row.TenantKey == tenant && row.Id == id, cancellationToken);
            if (entity is null) return AuthenticationSessionRevocationStatus.Missing;
            _ = Read(entity, tenantId);
            if (entity.RevokedAtMilliseconds is not null) return AuthenticationSessionRevocationStatus.AlreadyRevoked;
            if (entity.Version != expectedRevision) return AuthenticationSessionRevocationStatus.Conflict;
            var timestamp = now.ToUnixTimeMilliseconds(); var version = NewVersion();
            var changed = await context.Sessions.Where(row => row.TenantKey == tenant && row.Id == id && row.Version == expectedRevision && row.RevokedAtMilliseconds == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAtMilliseconds, timestamp).SetProperty(row => row.Version, version), cancellationToken);
            return changed == 1 ? AuthenticationSessionRevocationStatus.Revoked : AuthenticationSessionRevocationStatus.Conflict;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not EfSchemaVersionSkewException and not AuthenticationStoreException)
        {
            throw new AuthenticationStoreException("revoke-session", tenantId, id, exception);
        }
    }

    private AuthenticationSessionRecord Read(AuthenticationSessionEntity entity, string tenantId)
    {
        EfSchemaVersion.EnsureReadable(ExternalAuthenticationEfModule.Chain, entity.SchemaVersion);
        AuthenticationSessionRecord? record;
        try { record = JsonSerializer.Deserialize<AuthenticationSessionRecord>(Protector(entity).Unprotect(entity.ProtectedPayload)); }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        { throw new AuthenticationStoreException("read-session", tenantId, entity.Id, exception); }
        if (record is null || record.TenantId != tenantId || record.Id != entity.Id || Hash(record.UserId) != entity.UserKey ||
            record.CreatedAt.ToUnixTimeMilliseconds() != entity.CreatedAtMilliseconds || record.ExpiresAt.ToUnixTimeMilliseconds() != entity.ExpiresAtMilliseconds ||
            record.RevokedAt is not null || record.Revision is not null)
            throw new InvalidOperationException("Authentication session binding is invalid.");
        return record with { Revision = entity.Version,
            RevokedAt = entity.RevokedAtMilliseconds is { } revoked ? DateTimeOffset.FromUnixTimeMilliseconds(revoked) : null };
    }

    private IDataProtector Protector(AuthenticationSessionEntity entity) => protection.CreateProtector("Elsa.AuthenticationSession.v1",
        entity.TenantKey, entity.Id, entity.UserKey, entity.CreatedAtMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        entity.ExpiresAtMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private async Task Cleanup(string tenant, long now, CancellationToken token)
    {
        var expired = await context.Sessions.AsNoTracking().Where(row => row.TenantKey == tenant && row.ExpiresAtMilliseconds <= now && row.SchemaVersion == ExternalAuthenticationEfModule.SchemaVersion)
            .OrderBy(row => row.ExpiresAtMilliseconds).ThenBy(row => row.Id).Select(row => row.Id).Take(64).ToArrayAsync(token);
        if (expired.Length > 0) await context.Sessions.Where(row => row.TenantKey == tenant && expired.Contains(row.Id) &&
            row.ExpiresAtMilliseconds <= now && row.SchemaVersion == ExternalAuthenticationEfModule.SchemaVersion).ExecuteDeleteAsync(token);
    }

    private void Prepare(string tenantId, string? id, CancellationToken token)
    {
        if (!Text(tenantId, 400) || id is not null && !Hex(id)) throw new ArgumentException("Invalid authentication session scope.");
        access.Current.EnsureScope(new PersistenceScope(tenantId));
        context.EnsureProviderBinding();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Authentication sessions require independently committed operations.");
        token.ThrowIfCancellationRequested();
    }
    private static string Hash(string value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> unit = stackalloc byte[sizeof(char)];
        foreach (var character in value) { BinaryPrimitives.WriteUInt16BigEndian(unit, character); hash.AppendData(unit); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static string NewVersion() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private static bool Text(string? value, int length) => !string.IsNullOrWhiteSpace(value) && value.Length <= length && !value.Any(char.IsControl);
    private static bool Hex(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
