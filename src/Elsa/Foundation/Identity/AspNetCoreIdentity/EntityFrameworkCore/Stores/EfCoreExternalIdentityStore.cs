using Elsa.Foundation.Identity.Abstractions.Iam;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;

/// <summary>
/// Projects Elsa's <see cref="IExternalIdentityStore"/> over the ASP.NET Core Identity
/// <see cref="IdentityUserLogin{TKey}"/> table. The Identity <c>LoginProvider</c> carries the Elsa provider,
/// <c>ProviderKey</c> carries the provider subject, and the tenant is looked up via the owning user.
/// </summary>
public sealed class EfCoreExternalIdentityStore(ApplicationIdentityDbContext db) : IExternalIdentityStore
{
    public async ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        var login = await db.UserLogins.FirstOrDefaultAsync(x => x.LoginProvider == provider && x.ProviderKey == providerSubject, cancellationToken);
        if (login is null)
            return null;

        var belongsToTenant = await db.Users.AnyAsync(x => x.Id == login.UserId && x.TenantId == tenantId, cancellationToken);
        return belongsToTenant ? Map(tenantId, login) : null;
    }

    public async ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var logins = await db.UserLogins.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        return logins.Select(x => Map(tenantId, x)).ToList();
    }

    public async ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default)
    {
        var login = await db.UserLogins.FirstOrDefaultAsync(
            x => x.LoginProvider == externalIdentity.Provider && x.ProviderKey == externalIdentity.ProviderSubject,
            cancellationToken);

        if (login is null)
        {
            login = new IdentityUserLogin<string>
            {
                LoginProvider = externalIdentity.Provider,
                ProviderKey = externalIdentity.ProviderSubject
            };
            db.UserLogins.Add(login);
        }

        login.UserId = externalIdentity.UserId;
        login.ProviderDisplayName = externalIdentity.Provider;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ExternalIdentityRecord Map(string tenantId, IdentityUserLogin<string> login) =>
        new(
            tenantId,
            login.LoginProvider,
            login.ProviderKey,
            login.UserId,
            DateTimeOffset.UnixEpoch,
            null,
            ExternalIdentityLinkPolicy.Auto);
}
