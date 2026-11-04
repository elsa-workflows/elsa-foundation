namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

internal sealed record ManagedIdentityUsersResponse(string TenantId, string ProtocolPath, string OwnershipMode,
    bool CanManageUsers, bool CanRevokeTokens, bool CanReadLinks, bool CanReadSessions,
    string? LinkManagementUnavailableReason, string? SessionAdministrationUnavailableReason,
    IReadOnlyList<ManagedUserLookup> Items, long TotalCount);

internal sealed record ManagedSessionResponse(string Id, string UserId, string Provider, string? ConnectionKey,
    string? ConnectionRecordId, string? Issuer, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt, string State, string Revision, bool Current, bool CanRevoke);
internal sealed record ManagedSessionPage(string UserId, IReadOnlyList<ManagedSessionResponse> Items, long TotalCount);
internal sealed record ManagedSessionRevokeRequest(string ExpectedRevision);
internal sealed record ManagedSessionRevokeResponse(bool Revoked, bool AlreadyRevoked = false, string? Failure = null);
internal sealed record ManagedIdentityLinkResponse(string Provider, string? Issuer, string Subject, DateTimeOffset LinkedAt,
    DateTimeOffset? LastSeenAt, string? Revision, bool Qualified, bool ConnectionConfigured, string? ConnectionDisplayName, bool CanRemove = false);
internal sealed record ManagedLinkConnectionChoice(string Id, string Key, string DisplayName, bool Effective, bool Enabled);
internal sealed record ManagedIdentityLinkPage(string UserId, IReadOnlyList<ManagedIdentityLinkResponse> Items, long TotalCount,
    string? UserRevision, string? CatalogRevision, bool CanAttach, IReadOnlyList<ManagedLinkConnectionChoice> AttachChoices);
internal sealed record ManagedLinkAttachRequest(string ConnectionRecordId, string Subject, string? ExpectedCatalogRevision, string ExpectedUserRevision);
internal sealed record ManagedLinkAttachResponse(bool Attached, string? Failure = null);
internal sealed record ManagedLinkRemoveRequest(string Provider, string Issuer, string Subject, string ExpectedLinkRevision,
    string ExpectedUserRevision, string? ExpectedCatalogRevision);
internal sealed record ManagedLinkRemoveResponse(bool Removed, string? Failure = null);
