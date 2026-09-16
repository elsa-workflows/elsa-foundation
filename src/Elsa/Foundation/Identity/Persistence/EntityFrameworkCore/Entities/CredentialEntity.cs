using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// Relational projection of a tenant-local Identity credential. <see cref="Id"/> is a stable,
/// normalized tenant/record storage key; the original tenant and credential values are retained
/// separately so a read never rewrites contract data.
/// </summary>
public sealed class CredentialEntity
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string TenantLookupKey { get; set; } = "";
    public string CredentialId { get; set; } = "";
    public string CredentialLookupKey { get; set; } = "";
    public int SubjectType { get; set; }
    public string SubjectId { get; set; } = "";
    public int Kind { get; set; }
    public string HashedSecret { get; set; } = "";
    public string HashAlgorithm { get; set; } = "";
    public int Status { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public long Revision { get; set; }
}
