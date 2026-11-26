using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

public sealed class InMemoryIdentityStore : IUserStore, IRoleStore, IExternalIdentityStore, ITenantMembershipStore
{
    public bool SupportsQualifiedKeys => true;
    public bool SupportsAtomicAdmission => true;

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
        {
            var matches = _users.Where(x => Same(x.TenantId, tenantId) && Same(x.Email, email)).Take(2).ToArray();
            return ValueTask.FromResult(matches.Length == 1 ? matches[0] : null);
        }
    }

    public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            Upsert(_users, user, x => Same(x.TenantId, user.TenantId) && Same(x.Id, user.Id));

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> SetStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var index = _users.FindIndex(x => Same(x.TenantId, tenantId) && Same(x.Id, userId));
            if (index < 0)
                return ValueTask.FromResult(false);
            _users[index] = _users[index] with { Status = status };
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> DeleteAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            // As in the EF store, the user's external logins and tenant membership go with it.
            _externalIdentities.RemoveAll(x => Same(x.TenantId, tenantId) && Same(x.UserId, userId));
            _memberships.RemoveAll(x => Same(x.TenantId, tenantId) && Same(x.UserId, userId));
            return ValueTask.FromResult(_users.RemoveAll(x => Same(x.TenantId, tenantId) && Same(x.Id, userId)) > 0);
        }
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
                x.Issuer is null && Same(x.TenantId, tenantId) &&
                Same(x.Provider, provider) &&
                Same(x.ProviderSubject, providerSubject)));
        }
    }

    public ValueTask<ExternalIdentityRecord?> FindByKeyAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
            return ValueTask.FromResult(_externalIdentities.FirstOrDefault(x =>
                Same(x.TenantId, key.TenantId) && Same(x.Provider, key.Provider) &&
                string.Equals(x.Issuer, key.Issuer, StringComparison.Ordinal) &&
                string.Equals(x.ProviderSubject, key.Subject, StringComparison.Ordinal)));
    }

    public ValueTask<ExternalUserAdmissionResult> AdmitAsync(ExternalUserAdmission request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Key);
        ArgumentNullException.ThrowIfNull(request.User);
        if (!Same(request.Key.TenantId, request.User.TenantId))
            throw new ArgumentException("Admission user and identity must belong to the same tenant.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        var user = request.User with
        {
            RoleIds = request.User.RoleIds.ToHashSet(StringComparer.Ordinal),
            DirectPermissions = request.User.DirectPermissions.ToHashSet(StringComparer.Ordinal)
        };
        var link = new ExternalIdentityRecord(request.Key.TenantId, request.Key.Provider, request.Key.Subject,
            user.Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, request.LinkPolicy) { Issuer = request.Key.Issuer };
        lock (_syncRoot)
        {
            var existing = _externalIdentities.FirstOrDefault(x => SameLogin(x, link));
            if (existing is not null)
            {
                var owner = _users.FirstOrDefault(x => Same(x.TenantId, existing.TenantId) && Same(x.Id, existing.UserId));
                return ValueTask.FromResult(owner?.Status == UserStatus.Active
                    ? new ExternalUserAdmissionResult(true, owner.Id)
                    : new(false, Failure: "user-unavailable"));
            }
            if (user.Status != UserStatus.Active) return ValueTask.FromResult(new ExternalUserAdmissionResult(false, Failure: "user-unavailable"));
            if (request.CreateUser)
            {
                if (_users.Any(x => Same(x.TenantId, user.TenantId) && (Same(x.Id, user.Id) || Same(x.UserName, user.UserName))))
                    return ValueTask.FromResult(new ExternalUserAdmissionResult(false, Failure: "admission-conflict"));
                if (user.RoleIds.Any(role => !_roles.Any(x => Same(x.TenantId, user.TenantId) && Same(x.Id, role))))
                    return ValueTask.FromResult(new ExternalUserAdmissionResult(false, Failure: "role-unavailable"));
                _users.Add(user);
            }
            else
            {
                var current = _users.FirstOrDefault(x => Same(x.TenantId, user.TenantId) && Same(x.Id, user.Id));
                if (current is null || current.Status != UserStatus.Active)
                    return ValueTask.FromResult(new ExternalUserAdmissionResult(false, Failure: "user-unavailable"));
                if (current != user with { RoleIds = current.RoleIds, DirectPermissions = current.DirectPermissions } ||
                    !current.RoleIds.SetEquals(user.RoleIds) || !current.DirectPermissions.SetEquals(user.DirectPermissions))
                    return ValueTask.FromResult(new ExternalUserAdmissionResult(false, Failure: "admission-conflict"));
                var membership = _memberships.FirstOrDefault(x => Same(x.TenantId, user.TenantId) && Same(x.UserId, user.Id));
                var expected = request.ExpectedMembership;
                if (membership is null ? expected is not null : expected is null ||
                    membership != expected with { RoleIds = membership.RoleIds, DirectPermissions = membership.DirectPermissions } ||
                    !membership.RoleIds.SetEquals(expected.RoleIds) || !membership.DirectPermissions.SetEquals(expected.DirectPermissions))
                    return ValueTask.FromResult(new ExternalUserAdmissionResult(false, Failure: "admission-conflict"));
            }
            _externalIdentities.Add(link);
            return ValueTask.FromResult(new ExternalUserAdmissionResult(true, user.Id));
        }
    }

    public ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
            return ValueTask.FromResult<IReadOnlyList<ExternalIdentityRecord>>(_externalIdentities.Where(x => Same(x.TenantId, tenantId) && Same(x.UserId, userId)).ToList());
    }

    public ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalIdentity);
        if (externalIdentity.Issuer is not null) ArgumentException.ThrowIfNullOrWhiteSpace(externalIdentity.Issuer);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var existing = _externalIdentities.FirstOrDefault(x => SameLogin(x, externalIdentity));
            if (existing is not null && !Same(existing.UserId, externalIdentity.UserId))
                throw new InvalidOperationException("External identity is already linked to another user.");
            Upsert(
                _externalIdentities,
                externalIdentity,
                x => SameLogin(x, externalIdentity));
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

    private static bool SameLogin(ExternalIdentityRecord left, ExternalIdentityRecord right) =>
        Same(left.TenantId, right.TenantId) && Same(left.Provider, right.Provider) &&
        string.Equals(left.Issuer, right.Issuer, StringComparison.Ordinal) &&
        (left.Issuer is null ? Same(left.ProviderSubject, right.ProviderSubject) :
            string.Equals(left.ProviderSubject, right.ProviderSubject, StringComparison.Ordinal));

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
