using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// Relational projection shared by the tenant and global provider-configuration tables. Canonical
/// lookup values are persisted separately from the original values so reads do not depend on a
/// database's collation and round trips do not rewrite user-supplied provider names or tenants.
/// </summary>
[NotMapped]
public abstract class ProviderConfigurationEntity
{
    public string Id { get; set; } = "";
    public string? TenantId { get; set; }
    public string? TenantLookupKey { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderLookupKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
    public bool SupportsLocalUserManagement { get; set; }
    public bool SupportsLocalRoleManagement { get; set; }
    public bool SupportsApplicationManagement { get; set; }
    public bool SupportsGroupSync { get; set; }
    public bool SupportsTokenIssuance { get; set; }
    public bool SupportsRefresh { get; set; }
    public bool SupportsRevocation { get; set; }
    public int PermissionPropagation { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public long Revision { get; set; }
}

public sealed class TenantProviderConfigurationEntity : ProviderConfigurationEntity;

public sealed class GlobalProviderConfigurationEntity : ProviderConfigurationEntity;
