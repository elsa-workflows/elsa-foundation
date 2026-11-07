using System.Security.Claims;
using System.Text.Json.Serialization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Secrets.Core.Models;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Core;

/// <summary>A full source-owned provider document. Logical Key, not Id, identifies long-lived links.</summary>
public sealed record ConnectionRecord
{
    public string Id { get; init; } = "";
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Protocol { get; init; } = "oidc";
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, SecretReference> Secrets { get; init; } = new Dictionary<string, SecretReference>();
    public AdmissionPolicy Admission { get; init; } = new();
    public bool Enabled { get; init; }
    public bool Preferred { get; init; }
    public int DisplayOrder { get; init; }
    public bool OverridesDeployment { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
}

/// <summary>Installed admission policy selection; role IDs always refer to Elsa role definitions.</summary>
public sealed record AdmissionPolicy
{
    public string Type { get; init; } = "reject";
    public IReadOnlyList<string> DefaultRoleIds { get; init; } = [];
    public string? Matcher { get; init; }
}

public enum ConnectionSource { Deployment, Database }

public sealed record ConnectionView(
    ConnectionRecord Record,
    ConnectionSource Source,
    bool Effective,
    string? ShadowedById,
    bool OverridePermitted,
    string MaterialRevision);

public sealed record ConnectionCatalog(IReadOnlyList<ConnectionRecord> Records, string? Revision = null)
{
    /// <summary>Server-owned snapshot checks for an administrative write; never supplied through management JSON.</summary>
    [JsonIgnore]
    public IReadOnlyList<IamRevisionGuard>? IdentityRevisionGuards { get; init; }
}
public sealed record ConnectionRegistryView(IReadOnlyList<ConnectionView> Connections, string? Revision, bool DatabaseManagement);

[ReplacementContract]
public interface IConnectionCatalogStore
{
    bool SupportsIdentityRevisionGuards => false;
    /// <summary>Prepares the unchanged global provider CAS for an atomic qualified-link administration operation.</summary>
    ProviderConfigurationWrite? PrepareProviderWrite(ConnectionCatalog catalog) => null;
    ValueTask<ConnectionCatalog> ReadAsync(CancellationToken cancellationToken = default);
    /// <summary>Atomically saves the complete proposed catalog, only at its expected revision.</summary>
    ValueTask<CatalogSaveResult> SaveAsync(ConnectionCatalog catalog, CancellationToken cancellationToken = default);
}

public sealed record CatalogSaveResult(bool Saved, string? Revision = null);

[ReplacementContract]
public interface IConnectionRegistry
{
    ValueTask<ConnectionRegistryView> ReadAsync(CancellationToken cancellationToken = default);
}

public enum ConnectionChange { Create, Update, CreateOverride, PromoteOverride, Enable, Disable, Archive, Restore, Remove }
public sealed record ConnectionWrite(ConnectionChange Change, string? ExpectedRevision, ConnectionRecord Record);
public sealed record ConnectionWriteResult(bool Saved, string? Revision = null, string? RecordId = null, string? Failure = null);

/// <summary>Proposed source/lifecycle effect for confirmation; assessment never saves the catalog.</summary>
public sealed record ConnectionChangeAssessment(bool CanApply, ConnectionRegistryView? Proposed = null, string? Failure = null);

/// <summary>Authorizes host-wide administration and role delegation before a catalog mutation.</summary>
[ReplacementContract]
public interface IConnectionWriteAuthorizer
{
    ValueTask<bool> AuthorizeAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    ValueTask<bool> AuthorizePolicyAsync(ClaimsPrincipal actor, ConnectionRecord proposed, CancellationToken cancellationToken = default);
    /// <summary>Rechecks current authorization and returns the IAM roots it depends on; null refuses coordinated writes.</summary>
    ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ClaimsPrincipal actor, ConnectionRecord? proposed,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<IamRevisionGuard>?>(null);
}

/// <summary>Checks installed protocol, required configuration and current secret resolution without network sign-in.</summary>
[ReplacementContract]
public interface IConnectionReadiness
{
    ValueTask<bool> IsReadyAsync(ConnectionRecord connection, CancellationToken cancellationToken = default);
    async ValueTask<ConnectionReadinessResult> AssessAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) =>
        new(await IsReadyAsync(connection, cancellationToken) ? ConnectionReadinessFailure.None : ConnectionReadinessFailure.Unavailable);
}

public enum ConnectionReadinessFailure
{
    None, Unavailable, InvalidConfiguration, ProtocolUnavailable, ProtocolConfiguration, SecretUnavailable,
    CallbackUnavailable, SessionUnavailable, ArtifactStoreUnavailable, IdentityUnavailable, PolicyUnavailable,
    PolicyInvalid, RoleUnavailable, RoleForbidden
}
public sealed record ConnectionReadinessResult(ConnectionReadinessFailure Failure = ConnectionReadinessFailure.None,
    ExternalProtocolFailure ProtocolFailure = ExternalProtocolFailure.None)
{
    public bool Ready => Failure == ConnectionReadinessFailure.None;
}

/// <summary>Server-only successful normal sign-in evidence; preview never calls this contract.</summary>
[ReplacementContract]
public interface IConnectionSignInEvidence
{
    ValueTask RecordAsync(string userId, ExternalAuthenticationProvenance provenance, CancellationToken cancellationToken = default);
}

/// <summary>Reports an external path with an eligible recovery identity, separate from valid protocol configuration.</summary>
[ReplacementContract]
public interface IConnectionRecovery
{
    ValueTask<bool> IsUsableAsync(ConnectionRecord connection, CancellationToken cancellationToken = default);
    ValueTask<bool> IsUsableForAsync(ConnectionRecord connection, ClaimsPrincipal actor, CancellationToken cancellationToken = default) => IsUsableAsync(connection, cancellationToken);
    /// <summary>Returns the live IAM roots of a proved recovery identity; null means no coordinated recovery path.</summary>
    ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ConnectionRecord connection, ClaimsPrincipal actor,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<IamRevisionGuard>?>(null);
}

/// <summary>Reports a genuinely usable local recovery path, not merely an enabled login option.</summary>
[ReplacementContract]
public interface ILocalLoginRecovery
{
    ValueTask<bool> IsUsableAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> IsUsableForAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) => IsUsableAsync(cancellationToken);
    ValueTask<IReadOnlyList<IamRevisionGuard>?> CaptureRevisionGuardsAsync(ClaimsPrincipal actor,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<IamRevisionGuard>?>(null);
}

[ReplacementContract]
public interface IConnectionWriter
{
    ValueTask<ConnectionChangeAssessment> AssessAsync(ConnectionWrite request, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    ValueTask<ConnectionWriteResult> ApplyAsync(ConnectionWrite request, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}
