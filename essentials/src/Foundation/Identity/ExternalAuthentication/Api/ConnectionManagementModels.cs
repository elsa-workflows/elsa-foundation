using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

internal sealed record ManagedConnectionResponse(ConnectionRecord Document, ConnectionSource Source, bool Effective,
    string? ShadowedById, bool OverridePermitted, string MaterialRevision, bool ConfigurationEditable, bool ConfigurationSupported,
    string? SignInCallback, string? PreviewCallback, ConnectionReadinessResult? Readiness = null);
internal sealed record ManagedConnectionCatalogResponse(string TenantId, string ProtocolPath, string? Revision, bool DatabaseManagement,
    bool CanManageConnections, IReadOnlyList<string> ProvisioningPolicies,
    EffectiveProviderCapabilities Capabilities, IReadOnlyList<ManagedConnectionResponse> Connections, string? ManagementUnavailableReason = null);
internal sealed record ManagedConnectionDetailResponse(string? Revision, ManagedConnectionResponse Connection);
internal sealed record ManagedConnectionAssessmentResponse(bool CanApply, string? Failure, ManagedConnectionCatalogResponse? Proposed);
internal sealed record ManagedConnectionTestRequest(string ConnectionRecordId, string? ExpectedRevision);
internal sealed record ManagedConnectionTestResponse(string ConnectionRecordId, string MaterialRevision, DateTimeOffset ObservedAt,
    ConnectionReadinessResult Readiness, ExternalProtocolFailure ProtocolFailure, bool Stale);
internal sealed record ManagedUserLookup(string Id, string UserName, string? DisplayName, UserStatus Status);
internal sealed record ManagedRoleLookup(string Id, string Name, bool CanDelegate);
internal sealed record ManagedUserPage(IReadOnlyList<ManagedUserLookup> Items, long TotalCount);
internal sealed record ManagedRolePage(IReadOnlyList<ManagedRoleLookup> Items, long TotalCount);
internal sealed record ManagedConnectionError(string Error);
