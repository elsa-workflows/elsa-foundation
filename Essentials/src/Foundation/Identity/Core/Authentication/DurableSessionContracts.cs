using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Foundation.Identity.Core.Authentication;

/// <summary>Server-owned immutable session binding. SecurityVersion is an opaque account-version binding, never an administration projection.</summary>
public sealed record AuthenticationSessionRecord(
    string Id, string TenantId, string UserId, string Provider, string SecurityVersion,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
{
    public string? ConnectionKey { get; init; }
    public string? ConnectionRecordId { get; init; }
    public string? Issuer { get; init; }
    public string? ExternalSubject { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public string? Revision { get; init; }
}

public enum AuthenticationSessionRevocationStatus { Revoked, AlreadyRevoked, Missing, Conflict }

/// <summary>Committed tenant-local session authority; create and revoke must reject caller-owned transactions.</summary>
[ReplacementContract]
public interface IAuthenticationSessionStore
{
    bool IsDurable { get; }
    ValueTask<bool> TryCreateAsync(AuthenticationSessionRecord session, CancellationToken cancellationToken = default);
    ValueTask<AuthenticationSessionRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default);
    ValueTask<IamPage<AuthenticationSessionRecord>> ListAsync(string tenantId, string userId, IamPageRequest page, CancellationToken cancellationToken = default);
    ValueTask<AuthenticationSessionRevocationStatus> RevokeAsync(string tenantId, string id, string expectedRevision, DateTimeOffset now, CancellationToken cancellationToken = default);
}
