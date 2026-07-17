namespace Elsa.Persistence.Core;

/// <summary>
/// Immutable scope and authorization selected before a persistence operation reaches a provider.
/// </summary>
public sealed record PersistenceAccessContext
{
    private PersistenceAccessContext(
        PersistenceScope? scope,
        PersistenceAccessPolicy accessPolicy,
        PersistenceAccessPurpose? purpose,
        bool acrossScopes)
    {
        if (acrossScopes && scope is not null)
            throw new ArgumentException("Across-scopes access cannot also bind one persistence scope.", nameof(scope));
        if (accessPolicy == PersistenceAccessPolicy.Ordinary && (purpose is not null || acrossScopes))
            throw new ArgumentException("Ordinary persistence access cannot declare privileged purpose or cross-scope access.", nameof(accessPolicy));
        if (accessPolicy == PersistenceAccessPolicy.Privileged && purpose is null)
            throw new ArgumentNullException(nameof(purpose), "Privileged persistence access requires a named purpose.");

        Scope = scope;
        AccessPolicy = accessPolicy;
        Purpose = purpose;
        AcrossScopes = acrossScopes;
    }

    /// <summary>The selected tenant partition, or <see langword="null"/> for explicit global/across-scope access.</summary>
    public PersistenceScope? Scope { get; }

    public PersistenceAccessPolicy AccessPolicy { get; }

    public PersistenceAccessPurpose? Purpose { get; }

    public bool AcrossScopes { get; }

    public bool IsGlobal => Scope is null && !AcrossScopes;

    /// <summary>
    /// Verifies that an explicit domain scope agrees with the immutable persistence scope selected
    /// for this request. The exception intentionally omits both scope values so a rejected request
    /// cannot disclose another partition's identity.
    /// </summary>
    public void EnsureScope(PersistenceScope requiredScope)
    {
        ArgumentNullException.ThrowIfNull(requiredScope);

        if (Scope != requiredScope)
            throw new InvalidOperationException("The requested resource does not belong to the current persistence scope.");
    }

    /// <summary>
    /// Verifies an explicit tenant identity carried by a provider-neutral domain object. A missing
    /// tenant identity does not imply global access; the already-selected nonblank persistence scope
    /// remains authoritative for that operation.
    /// </summary>
    public void EnsureTenantScope(string? tenantId)
    {
        if (tenantId is null)
            return;

        EnsureScope(new PersistenceScope(tenantId));
    }

    public static PersistenceAccessContext Scoped(PersistenceScope scope) =>
        new(scope ?? throw new ArgumentNullException(nameof(scope)), PersistenceAccessPolicy.Ordinary, null, false);

    public static PersistenceAccessContext Global { get; } =
        new(null, PersistenceAccessPolicy.Ordinary, null, false);

    public static PersistenceAccessContext PrivilegedScoped(
        PersistenceScope scope,
        PersistenceAccessPurpose purpose) =>
        new(
            scope ?? throw new ArgumentNullException(nameof(scope)),
            PersistenceAccessPolicy.Privileged,
            purpose ?? throw new ArgumentNullException(nameof(purpose)),
            false);

    public static PersistenceAccessContext PrivilegedGlobal(PersistenceAccessPurpose purpose) =>
        new(null, PersistenceAccessPolicy.Privileged, purpose ?? throw new ArgumentNullException(nameof(purpose)), false);

    public static PersistenceAccessContext PrivilegedAcrossScopes(PersistenceAccessPurpose purpose) =>
        new(null, PersistenceAccessPolicy.Privileged, purpose ?? throw new ArgumentNullException(nameof(purpose)), true);
}
