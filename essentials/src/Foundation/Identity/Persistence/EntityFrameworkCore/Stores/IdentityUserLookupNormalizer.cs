namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Produces the normalized user name and email that the EF user row stores and keys its lookups on. Saves and
/// lookups (find by email, search) derive their keys through the same normalizer, so a user is found by the
/// value it was saved with. A host that also writes users through ASP.NET Core Identity registers an adapter
/// over its <c>ILookupNormalizer</c>, so both write paths and every lookup agree on the key.
/// </summary>
public interface IIdentityUserLookupNormalizer
{
    /// <summary>Returns the normalized user name, or <c>null</c> when it has none.</summary>
    string? NormalizeUserName(string userName);

    /// <summary>Returns the normalized email, or <c>null</c> when it has none.</summary>
    string? NormalizeEmail(string email);
}

/// <summary>The normalizer used when the host registers none: invariant lowercasing of the raw value.</summary>
public sealed class IdentityUserLookupNormalizer : IIdentityUserLookupNormalizer
{
    public static IdentityUserLookupNormalizer Invariant { get; } = new();

    private IdentityUserLookupNormalizer() { }

    public string? NormalizeUserName(string userName) => EfIdentityStoreSupport.Normalize(userName);

    public string? NormalizeEmail(string email) => EfIdentityStoreSupport.Normalize(email);
}
