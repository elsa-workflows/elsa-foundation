namespace Elsa.Primitives.Entities;

/// <summary>
/// Base class for entities scoped to a tenant. Carries <see cref="TenantId"/>; the
/// <c>TenantId</c> index is registered centrally by <c>ElsaDbContextBase.ApplyTenantIdIndex</c>,
/// so per-entity EF configurations MUST NOT declare a <c>TenantId</c> index.
/// </summary>
public abstract class TenantEntity : Entity
{
    /// <summary>
    /// Gets or sets the ID of the tenant that owns this entity.
    /// </summary>
    public string? TenantId { get; set; }
}