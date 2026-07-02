using Elsa.Foundation.Identity.Abstractions.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

public sealed class InMemoryIdentityStore : IUserStore, IRoleStore, IExternalIdentityStore, ITenantMembershipStore
{
    private readonly object _syncRoot = new();
    private readonly List<UserRecord> _users = [];
    private readonly List<RoleRecord> _roles = [];
    private readonly List<ExternalIdentityRecord> _externalIdentities = [];
    private readonly List<TenantMembershipRecord> _memberships = [];

    ValueTask<UserRecord?> IUserStore.FindAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
            return ValueTask.FromResult(_users.FirstOrDefault(x => Same(x.TenantId, tenantId) && Same(x.Id, userId)));
    }

    public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            return ValueTask.FromResult(_users.FirstOrDefault(x => Same(x.TenantId, tenantId) && Same(x.Email, email)));
    }

    public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            Upsert(_users, user, x => Same(x.TenantId, user.TenantId) && Same(x.Id, user.Id));

        return ValueTask.CompletedTask;
    }

    ValueTask<RoleRecord?> IRoleStore.FindAsync(string tenantId, string roleId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
            return ValueTask.FromResult(_roles.FirstOrDefault(x => Same(x.TenantId, tenantId) && Same(x.Id, roleId)));
    }

    public ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            return ValueTask.FromResult<IReadOnlyList<RoleRecord>>(_roles.Where(x => Same(x.TenantId, tenantId)).ToList());
    }

    public ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            Upsert(_roles, role, x => Same(x.TenantId, role.TenantId) && Same(x.Id, role.Id));

        return ValueTask.CompletedTask;
    }

    public ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return ValueTask.FromResult(_externalIdentities.FirstOrDefault(x =>
                Same(x.TenantId, tenantId) &&
                Same(x.Provider, provider) &&
                Same(x.ProviderSubject, providerSubject)));
        }
    }

    public ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            return ValueTask.FromResult<IReadOnlyList<ExternalIdentityRecord>>(_externalIdentities.Where(x => Same(x.TenantId, tenantId) && Same(x.UserId, userId)).ToList());
    }

    public ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            Upsert(
                _externalIdentities,
                externalIdentity,
                x => Same(x.TenantId, externalIdentity.TenantId) &&
                     Same(x.Provider, externalIdentity.Provider) &&
                     Same(x.ProviderSubject, externalIdentity.ProviderSubject));
        }

        return ValueTask.CompletedTask;
    }

    ValueTask<TenantMembershipRecord?> ITenantMembershipStore.FindAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
            return ValueTask.FromResult(_memberships.FirstOrDefault(x => Same(x.TenantId, tenantId) && Same(x.UserId, userId)));
    }

    public ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            Upsert(_memberships, membership, x => Same(x.TenantId, membership.TenantId) && Same(x.UserId, membership.UserId));

        return ValueTask.CompletedTask;
    }

    private static void Upsert<T>(List<T> items, T item, Func<T, bool> predicate)
    {
        var index = items.FindIndex(x => predicate(x));
        if (index >= 0)
            items[index] = item;
        else
            items.Add(item);
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
