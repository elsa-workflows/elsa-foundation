using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Microsoft.AspNetCore.Identity;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;

/// <summary>
/// Keys the Elsa user store's saves and lookups on ASP.NET Core Identity's <see cref="ILookupNormalizer"/>, the same
/// normalizer <see cref="UserManager{TUser}"/> applies before it writes, so both write paths produce one key.
/// </summary>
public sealed class EfCoreIdentityUserLookupNormalizer(ILookupNormalizer normalizer) : IIdentityUserLookupNormalizer
{
    public string? NormalizeUserName(string userName) => normalizer.NormalizeName(userName);

    public string? NormalizeEmail(string email) => normalizer.NormalizeEmail(email);
}
