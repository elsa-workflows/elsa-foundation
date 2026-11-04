using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authorization;
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
    public IReadOnlyList<RoleMapping> RoleMappings { get; init; } = [];
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

public sealed record RoleMapping(string ClaimType, string ClaimValue, string RoleId);
public enum ConnectionSource { Deployment, Database }

public sealed record ConnectionView(
    ConnectionRecord Record,
    ConnectionSource Source,
    bool Effective,
    string? ShadowedById,
    bool OverridePermitted,
    string MaterialRevision);

public sealed record ConnectionCatalog(IReadOnlyList<ConnectionRecord> Records, string? Revision = null);
public sealed record ConnectionRegistryView(IReadOnlyList<ConnectionView> Connections, string? Revision, bool DatabaseManagement);

[ReplacementContract]
public interface IConnectionCatalogStore
{
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
}

/// <summary>Checks installed protocol, required configuration and current secret resolution without network sign-in.</summary>
[ReplacementContract]
public interface IConnectionReadiness
{
    ValueTask<bool> IsReadyAsync(ConnectionRecord connection, CancellationToken cancellationToken = default);
}

/// <summary>Reports an external path with an eligible recovery identity, separate from valid protocol configuration.</summary>
[ReplacementContract]
public interface IConnectionRecovery
{
    ValueTask<bool> IsUsableAsync(ConnectionRecord connection, CancellationToken cancellationToken = default);
}

/// <summary>Reports a genuinely usable local recovery path, not merely an enabled login option.</summary>
[ReplacementContract]
public interface ILocalLoginRecovery
{
    ValueTask<bool> IsUsableAsync(CancellationToken cancellationToken = default);
}

[ReplacementContract]
public interface IConnectionWriter
{
    ValueTask<ConnectionChangeAssessment> AssessAsync(ConnectionWrite request, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    ValueTask<ConnectionWriteResult> ApplyAsync(ConnectionWrite request, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}
