using System.Text.Json;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>The retained classification of catalog material. Missing classification grants nothing.</summary>
public enum CatalogDisclosureKind
{
    Unspecified = 0,
    Public = 1,
    Protected = 2
}

/// <summary>A current decision, never a retained invocation grant.</summary>
public enum CatalogDisclosureStatus
{
    Unavailable = 0,
    Allowed = 1,
    Denied = 2
}

/// <summary>The catalog operation for which current disclosure is being checked.</summary>
public enum CatalogDisclosurePurpose
{
    Metadata,
    Construct,
    Options,
    Authoring,
    Historical
}

/// <summary>The canonical ownership category of stored catalog material.</summary>
public enum CatalogOwnerScopeKind
{
    Unspecified = 0,
    Global = 1,
    Tenant = 2
}

/// <summary>Canonical resource ownership; this is not authority to execute in a tenant.</summary>
public sealed record CatalogOwnerScope(CatalogOwnerScopeKind Kind, string? TenantId);

/// <summary>A retained requirement for one optional owner policy, with detached opaque resource facts.</summary>
public sealed record CatalogDisclosurePolicyRequirement
{
    public CatalogDisclosurePolicyRequirement(string policyKey, string bindingSchema, JsonElement resource)
    {
        PolicyKey = policyKey;
        BindingSchema = bindingSchema;
        Resource = resource.ValueKind == JsonValueKind.Undefined ? default : resource.Clone();
    }

    public string PolicyKey { get; }
    public string BindingSchema { get; }
    public JsonElement Resource { get; }
}

/// <summary>
/// Retained classification and conjunction. Construction takes a snapshot; admission still validates its shape.
/// </summary>
public sealed record CatalogDisclosureRequirement
{
    public CatalogDisclosureRequirement(int schemaVersion, CatalogDisclosureKind kind,
        IReadOnlyList<CatalogDisclosurePolicyRequirement> policies)
    {
        SchemaVersion = schemaVersion;
        Kind = kind;
        // Preserve a malformed null rather than turning a missing list into an explicit Public requirement.
        Policies = policies is null ? null! : Array.AsReadOnly(policies.ToArray());
    }

    public int SchemaVersion { get; }
    public CatalogDisclosureKind Kind { get; }
    public IReadOnlyList<CatalogDisclosurePolicyRequirement> Policies { get; }
}

/// <summary>Canonical loaded catalog facts supplied to a current policy.</summary>
public sealed record CatalogDisclosureResource(
    string Kind, string Id, CatalogOwnerScope OwnerScope, CatalogDisclosureRequirement Requirement);

/// <summary>One current disclosure result; only a validated explicit Allowed result grants access.</summary>
public sealed record CatalogDisclosureDecision(
    CatalogDisclosureStatus Status, string? AuthorizationRevision = null);

/// <summary>Lossless identity of one contributor to disclosed catalog metadata.</summary>
public sealed record CatalogDisclosureIdentity(
    string Kind, string? DefinitionId, string VersionId, CatalogOwnerScope OwnerScope);

/// <summary>A detached identity inventory; schema 2 provenance must also be certified by its owning contributor.</summary>
public sealed record CatalogDisclosureSnapshot
{
    public CatalogDisclosureSnapshot(int schemaVersion, IReadOnlyList<CatalogDisclosureIdentity> identities)
    {
        SchemaVersion = schemaVersion;
        Identities = identities is null ? null! : Array.AsReadOnly(identities.ToArray());
    }

    public int SchemaVersion { get; }
    public IReadOnlyList<CatalogDisclosureIdentity> Identities { get; }
}
