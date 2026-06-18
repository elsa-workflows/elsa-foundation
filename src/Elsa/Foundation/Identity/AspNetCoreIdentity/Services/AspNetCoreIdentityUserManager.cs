using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

public sealed class AspNetCoreIdentityUserManager(IUserStore users, IExternalIdentityStore externalIdentities) : IUserManager
{
    public async ValueTask<UserRecord> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = new UserRecord(
            Guid.NewGuid().ToString("n"),
            request.TenantId,
            request.UserName,
            request.Email,
            request.DisplayName,
            UserStatus.Active,
            request.Ownership,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        await users.SaveAsync(user, cancellationToken);
        return user;
    }

    public async ValueTask<UserRecord> LinkExternalIdentityAsync(LinkExternalIdentityRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await externalIdentities.FindBySubjectAsync(request.TenantId, request.Provider, request.ProviderSubject, cancellationToken);
        if (existing is not null && !string.Equals(existing.UserId, request.UserId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("External identity is already linked to another user.");

        var user = await users.FindAsync(request.TenantId, request.UserId, cancellationToken) ??
                   throw new InvalidOperationException("Cannot link external identity to a missing user.");

        await externalIdentities.SaveAsync(new ExternalIdentityRecord(
            request.TenantId,
            request.Provider,
            request.ProviderSubject,
            request.UserId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            request.LinkPolicy), cancellationToken);

        return user;
    }

    public async ValueTask DisableAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(tenantId, userId, cancellationToken) ??
                   throw new InvalidOperationException("Cannot disable a missing user.");

        await users.SaveAsync(user with { Status = UserStatus.Disabled }, cancellationToken);
    }
}
