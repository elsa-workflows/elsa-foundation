using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// Relational projection for one tenant-local Identity application. The raw identity values are
/// retained for lossless contract round trips while the normalized storage key makes lookups
/// independent of the provider's collation rules.
/// </summary>
public sealed class ApplicationEntity : IRevisionedIdentityEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ApplicationType Type { get; set; }
    public ResourceOwnership Ownership { get; set; }
    public string AllowedGrantTypesJson { get; set; } = "[]";
    public string ScopesJson { get; set; } = "[]";
    public long Revision { get; set; }
}
