namespace Elsa.Primitives.Entities;

/// <summary>
/// Base class for entities scoped to a tenant. Carries <see cref="TenantId"/>;
/// persistence implementations own the corresponding storage and index declarations.
/// </summary>
public abstract class TenantEntity : Entity
{
    /// <summary>
    /// Gets or sets the ID of the tenant that owns this entity.
    /// </summary>
    public string? TenantId { get; set; }
}
