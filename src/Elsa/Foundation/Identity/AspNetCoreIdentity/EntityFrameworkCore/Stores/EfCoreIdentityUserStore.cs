using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using EfIdentityStoreSupport = Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores.IdentityEntityFrameworkAdapterSupport;
using IdentityEntityFrameworkRevisionCodec = Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores.IdentityEntityFrameworkRevisionSupport;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;

/// <summary>
/// ASP.NET Core Identity's complete user-store surface over the Foundation Identity authority tables.
/// The framework store and Elsa's provider-neutral user store intentionally share one root row and one
/// transaction boundary; no <c>IdentityDbContext</c> or framework-owned schema is introduced.
/// </summary>
public sealed class EfCoreIdentityUserStore(
    IdentityIamDbContext db,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IIdentityEmailUniquenessPolicy? emailUniquenessPolicy = null,
    EfIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    EfIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null) :
    IUserPasswordStore<AspNetCoreIdentityUser>,
    IUserSecurityStampStore<AspNetCoreIdentityUser>,
    IUserEmailStore<AspNetCoreIdentityUser>,
    IUserLockoutStore<AspNetCoreIdentityUser>,
    IUserPhoneNumberStore<AspNetCoreIdentityUser>,
    IUserTwoFactorStore<AspNetCoreIdentityUser>,
    IUserLoginStore<AspNetCoreIdentityUser>,
    IUserClaimStore<AspNetCoreIdentityUser>,
    IUserRoleStore<AspNetCoreIdentityUser>,
    IUserAuthenticationTokenStore<AspNetCoreIdentityUser>,
    IUserAuthenticatorKeyStore<AspNetCoreIdentityUser>,
    IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>
{
    private const string AuthenticatorStoreLoginProvider = "[AspNetAuthenticatorStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodeTokenProvider = "[AspNetUserStore]";
    private const string RecoveryCodeTokenName = "RecoveryCodes";
    private const int AmbiguousEmailTake = 2;
    private const int MaximumMaterializedRelationshipEntries = 512;
    private const int LockoutTransitionMaxAttempts = 3;

    private readonly IIdentityEmailUniquenessPolicy emailPolicy =
        emailUniquenessPolicy ?? IdentityEmailUniquenessPolicy.NonUnique;
    private readonly EfIdentityAuthorityAggregateCoordinator aggregates =
        aggregateCoordinator ?? new(db, accessContextAccessor);
    private readonly EfIdentityAuthorityRelationshipCoordinator relationships =
        relationshipCoordinator ?? new(db, new EfIdentityAtomicWrite(db, accessContextAccessor: accessContextAccessor), accessContextAccessor);

    public void Dispose() { }

    public Task<string> GetUserIdAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(AspNetCoreIdentityUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(AspNetCoreIdentityUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserScope(user);
        return await SaveFrameworkUserAsync(user, expectedRevision: 0, createOnly: true, cancellationToken);
    }

    public async Task<IdentityResult> UpdateAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserScope(user);
        if (!IdentityEntityFrameworkRevisionCodec.TryGetUserVersion(user.ConcurrencyStamp, user.TenantId, user.Id, out var revision))
            return ConcurrencyFailure();

        return await SaveFrameworkUserAsync(user, revision, createOnly: false, cancellationToken);
    }

    public async Task<IdentityResult> DeleteAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserScope(user);
        if (!IdentityEntityFrameworkRevisionCodec.TryGetUserVersion(user.ConcurrencyStamp, user.TenantId, user.Id, out var revision))
            return ConcurrencyFailure();

        var result = await aggregates.DeleteUserAsync(user.TenantId, user.Id, revision, cancellationToken);
        if (result.Succeeded)
            user.ConcurrencyStamp = null;
        return ToIdentityResult(result);
    }

    public async Task<AspNetCoreIdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var entity = await FindEntityAsync(tenantId, userId, forWrite: false, cancellationToken);
        return entity is null ? null : ToFrameworkUser(entity);
    }

    public async Task<AspNetCoreIdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var key = UserKey(tenantId, normalizedUserName);
        var entity = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to find the ASP.NET Core Identity user by normalized name.",
            () => db.Users.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantLookupKey == TenantKey(tenantId) && x.NormalizedUserNameKey == key,
                cancellationToken));
        return entity is null ? null : ToFrameworkUser(entity);
    }

    public Task SetPasswordHashAsync(AspNetCoreIdentityUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash is not null);

    public Task SetSecurityStampAsync(AspNetCoreIdentityUser user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.SecurityStamp);

    public Task SetEmailAsync(AspNetCoreIdentityUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(AspNetCoreIdentityUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<AspNetCoreIdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var key = UserKey(tenantId, normalizedEmail);
        var entities = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to find the ASP.NET Core Identity user by normalized email.",
            () => db.Users.AsNoTracking()
                .Where(x => x.TenantLookupKey == TenantKey(tenantId) && x.NormalizedEmailKey == key)
                .OrderBy(x => x.Id)
                .Take(AmbiguousEmailTake)
                .ToListAsync(cancellationToken));
        return entities.Count == 1 ? ToFrameworkUser(entities[0]) : null;
    }

    public Task<string?> GetNormalizedEmailAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(AspNetCoreIdentityUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    public async Task SetLockoutEndDateAsync(AspNetCoreIdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken) =>
        await MutateLockoutAsync(user, current => { current.LockoutEnd = lockoutEnd; return current.AccessFailedCount; }, cancellationToken);

    public Task<int> IncrementAccessFailedCountAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        MutateLockoutAsync(user, current => ++current.AccessFailedCount, cancellationToken);

    public async Task ResetAccessFailedCountAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        await MutateLockoutAsync(user, current => { current.AccessFailedCount = 0; return 0; }, cancellationToken);

    public Task<int> GetAccessFailedCountAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public async Task SetLockoutEnabledAsync(AspNetCoreIdentityUser user, bool enabled, CancellationToken cancellationToken) =>
        await MutateLockoutAsync(user, current => { current.LockoutEnabled = enabled; return current.AccessFailedCount; }, cancellationToken);

    public Task SetPhoneNumberAsync(AspNetCoreIdentityUser user, string? phoneNumber, CancellationToken cancellationToken)
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumber);

    public Task<bool> GetPhoneNumberConfirmedAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(AspNetCoreIdentityUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task SetTwoFactorEnabledAsync(AspNetCoreIdentityUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.TwoFactorEnabled);

    public async Task<IList<Claim>> GetClaimsAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var rows = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to read ASP.NET Core Identity user claims.",
            () => db.UserClaims.AsNoTracking()
                    .Where(x => x.TenantLookupKey == TenantKey(user.TenantId) && x.UserLookupKey == UserKey(user.TenantId, user.Id))
                    .OrderBy(x => x.Id)
                    .Take(MaximumMaterializedRelationshipEntries + 1)
                    .ToListAsync(cancellationToken));
        EnsureRelationshipMaterializationLimit(rows.Count, "user claims");
        return rows
            .Select(x => new Claim(x.ClaimType, x.ClaimValue ?? string.Empty))
            .ToList();
    }

    public async Task AddClaimsAsync(AspNetCoreIdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        ArgumentNullException.ThrowIfNull(claims);
        var additions = claims.Take(MaximumMaterializedRelationshipEntries + 1).Select(claim => new UserClaimEntity
        {
            Id = EfIdentityStoreSupport.CompoundKey(user.TenantId, user.Id, claim.Type, claim.Value),
            TenantId = user.TenantId,
            TenantLookupKey = TenantKey(user.TenantId),
            UserId = user.Id,
            UserLookupKey = UserKey(user.TenantId, user.Id),
            ClaimType = claim.Type,
            ClaimValue = claim.Value,
            ClaimKey = EfIdentityStoreSupport.CompoundKey(user.TenantId, claim.Type, claim.Value),
            Revision = 1
        }).ToArray();
        EnsureRelationshipMaterializationLimit(additions.Length, "user claim input");
        if (additions.Length == 0)
            return;
        await WriteRelationshipAsync(user, additions, [], cancellationToken);
    }

    public async Task ReplaceClaimAsync(AspNetCoreIdentityUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var replacement = new UserClaimEntity
        {
            Id = EfIdentityStoreSupport.CompoundKey(user.TenantId, user.Id, newClaim.Type, newClaim.Value),
            TenantId = user.TenantId,
            TenantLookupKey = TenantKey(user.TenantId),
            UserId = user.Id,
            UserLookupKey = UserKey(user.TenantId, user.Id),
            ClaimType = newClaim.Type,
            ClaimValue = newClaim.Value,
            ClaimKey = EfIdentityStoreSupport.CompoundKey(user.TenantId, newClaim.Type, newClaim.Value),
            Revision = 1
        };
        await WriteRelationshipAsync(user, [replacement], [(claim.Type, (string?)claim.Value)], cancellationToken);
    }

    public async Task RemoveClaimsAsync(AspNetCoreIdentityUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        ArgumentNullException.ThrowIfNull(claims);
        var values = claims.Take(MaximumMaterializedRelationshipEntries + 1).Select(claim => (claim.Type, (string?)claim.Value)).ToArray();
        EnsureRelationshipMaterializationLimit(values.Length, "user claim input");
        if (values.Length == 0)
            return;
        await WriteRelationshipAsync(user, [], values, cancellationToken);
    }

    public async Task<IList<AspNetCoreIdentityUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var claimKey = EfIdentityStoreSupport.CompoundKey(tenantId, claim.Type, claim.Value);
        var entities = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to find ASP.NET Core Identity users for a claim.",
            () => db.Users.AsNoTracking()
                .Where(x => x.TenantLookupKey == TenantKey(tenantId))
                .Join(
                    db.UserClaims.AsNoTracking()
                        .Where(x => x.TenantLookupKey == TenantKey(tenantId) && x.ClaimKey == claimKey),
                    user => new { user.TenantLookupKey, UserLookupKey = user.Id },
                    relationship => new { relationship.TenantLookupKey, relationship.UserLookupKey },
                    (user, _) => user)
                // The tenant/user/claim-key uniqueness constraint guarantees one match per user;
                // avoid DISTINCT over the full entity (including provider-sized text/JSON fields).
                .OrderBy(x => x.UserIdOrderKey)
                .ThenBy(x => x.Id)
                .Take(MaximumMaterializedRelationshipEntries + 1)
                .ToListAsync(cancellationToken));
        EnsureRelationshipMaterializationLimit(entities.Count, "users for claim");
        return entities.Select(ToFrameworkUser).ToList();
    }

    public async Task AddLoginAsync(AspNetCoreIdentityUser user, UserLoginInfo login, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var expected = RequireRevision(user);
        var result = await relationships.SaveExternalIdentityAsync(
            new ExternalIdentityEntity
            {
                TenantId = user.TenantId,
                Provider = login.LoginProvider,
                ProviderDisplayName = login.ProviderDisplayName,
                ProviderSubject = login.ProviderKey,
                UserId = user.Id,
                LinkedAt = DateTimeOffset.UtcNow,
                LinkPolicy = (int)ExternalIdentityLinkPolicy.Admin,
                Revision = 1
            },
            expected,
            expectedLoginVersion: null,
            enforceLoginVersion: false,
            EfExternalLoginOwnershipPolicy.CreateOrSameOwner,
            returnOwnerResult: true,
            cancellationToken);
        ApplyRevisionStamp(user, result);
        EnsureRelationshipSucceeded(result, "external login add");
    }

    public async Task RemoveLoginAsync(AspNetCoreIdentityUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var result = await relationships.DeleteExternalIdentityAsync(
            user.TenantId,
            user.Id,
            loginProvider,
            providerKey,
            RequireRevision(user),
            cancellationToken);
        ApplyRevisionStamp(user, result);
        EnsureRelationshipSucceeded(result, "external login remove");
    }

    public async Task<IList<UserLoginInfo>> GetLoginsAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var rows = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to read ASP.NET Core Identity user logins.",
            () => db.ExternalIdentities.AsNoTracking()
                    .Where(x => x.TenantLookupKey == TenantKey(user.TenantId) && x.UserLookupKey == UserKey(user.TenantId, user.Id))
                    .OrderBy(x => x.Id).Take(MaximumMaterializedRelationshipEntries + 1).ToListAsync(cancellationToken));
        EnsureRelationshipMaterializationLimit(rows.Count, "external logins");
        return rows
            .Select(x => new UserLoginInfo(x.Provider, x.ProviderSubject, x.ProviderDisplayName))
            .ToList();
    }

    public async Task<AspNetCoreIdentityUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var id = EfIdentityStoreSupport.CompoundKey(tenantId, loginProvider, providerKey);
        var login = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to find the ASP.NET Core Identity user by login.",
            () => db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TenantLookupKey == TenantKey(tenantId), cancellationToken));
        return login is null ? null : await FindByIdAsync(login.UserId, cancellationToken);
    }

    public async Task AddToRoleAsync(AspNetCoreIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var role = await FindRoleAsync(user.TenantId, roleName, cancellationToken)
                   ?? throw new InvalidOperationException("The requested role does not exist in the current persistence scope.");
        var result = await relationships.AddUserRoleAsync(
            user.TenantId,
            user.Id,
            role.RoleId,
            RequireRevision(user),
            new UserRoleEntity { TenantId = user.TenantId, UserId = user.Id, RoleId = role.RoleId, Revision = 1 },
            cancellationToken);
        ApplyRevisionStamp(user, result);
        EnsureRelationshipSucceeded(result, "role add");
    }

    public async Task RemoveFromRoleAsync(AspNetCoreIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var role = await FindRoleAsync(user.TenantId, roleName, cancellationToken);
        if (role is null)
            return;
        var result = await relationships.DeleteUserRoleAsync(
            user.TenantId,
            user.Id,
            role.RoleId,
            RequireRevision(user),
            cancellationToken);
        ApplyRevisionStamp(user, result);
        EnsureRelationshipSucceeded(result, "role remove");
    }

    public async Task<IList<string>> GetRolesAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var roleIds = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to read ASP.NET Core Identity user roles.",
            () => db.UserRoles.AsNoTracking()
                .Where(x => x.TenantLookupKey == TenantKey(user.TenantId) && x.UserLookupKey == UserKey(user.TenantId, user.Id))
                .OrderBy(x => x.RoleLookupKey)
                .ThenBy(x => x.Id)
                .Select(x => x.RoleId)
                .Take(MaximumMaterializedRelationshipEntries + 1)
                .ToListAsync(cancellationToken));
        EnsureRelationshipMaterializationLimit(roleIds.Count, "user roles");
        var roleRecordIds = roleIds
            .Select(roleId => EfIdentityStoreSupport.RecordId(user.TenantId, roleId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to read ASP.NET Core Identity role names.",
            () => db.Roles.AsNoTracking()
                .Where(x => x.TenantLookupKey == TenantKey(user.TenantId) && roleRecordIds.Contains(x.Id))
                .OrderBy(x => x.RoleIdOrderKey)
                .ThenBy(x => x.Id)
                .Select(x => x.Name)
                .ToListAsync(cancellationToken));
    }

    public async Task<bool> IsInRoleAsync(AspNetCoreIdentityUser user, string roleName, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var role = await FindRoleAsync(user.TenantId, roleName, cancellationToken);
        return role is not null && await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to check ASP.NET Core Identity user role membership.",
            () => db.UserRoles.AnyAsync(x => x.Id == EfIdentityStoreSupport.CompoundKey(user.TenantId, user.Id, role.RoleId), cancellationToken));
    }

    public async Task<IList<AspNetCoreIdentityUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var role = await FindRoleAsync(tenantId, roleName, cancellationToken);
        if (role is null)
            return [];
        var roleLookupKey = RoleKey(tenantId, role.RoleId);
        var entities = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to read ASP.NET Core Identity users in a role.",
            () => db.Users.AsNoTracking()
                .Where(x => x.TenantLookupKey == TenantKey(tenantId))
                .Join(
                    db.UserRoles.AsNoTracking()
                        .Where(x => x.TenantLookupKey == TenantKey(tenantId) && x.RoleLookupKey == roleLookupKey),
                    user => new { user.TenantLookupKey, UserLookupKey = user.Id },
                    relationship => new { relationship.TenantLookupKey, relationship.UserLookupKey },
                    (user, _) => user)
                // The tenant/user/role-key uniqueness constraint guarantees one match per user;
                // avoid DISTINCT over the full entity (including provider-sized text/JSON fields).
                .OrderBy(x => x.UserIdOrderKey)
                .ThenBy(x => x.Id)
                .Take(MaximumMaterializedRelationshipEntries + 1)
                .ToListAsync(cancellationToken));
        EnsureRelationshipMaterializationLimit(entities.Count, "users in role");
        return entities.Select(ToFrameworkUser).ToList();
    }

    public async Task SetTokenAsync(AspNetCoreIdentityUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var result = await relationships.SaveUserTokenAsync(
            user.TenantId,
            user.Id,
            RequireRevision(user),
            new UserTokenEntity
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                LoginProvider = loginProvider,
                Name = name,
                Value = value,
                Revision = 1
            },
            cancellationToken);
        ApplyRevisionStamp(user, result);
        EnsureRelationshipSucceeded(result, "token save");
    }

    public async Task RemoveTokenAsync(AspNetCoreIdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var result = await relationships.DeleteUserTokenAsync(
            user.TenantId,
            user.Id,
            RequireRevision(user),
            loginProvider,
            name,
            cancellationToken);
        ApplyRevisionStamp(user, result);
        EnsureRelationshipSucceeded(result, "token remove");
    }

    public async Task<string?> GetTokenAsync(AspNetCoreIdentityUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        var token = await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to read ASP.NET Core Identity user token.",
            () => db.UserTokens.AsNoTracking().SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.CompoundKey(user.TenantId, user.Id, loginProvider, name), cancellationToken));
        return token?.Value;
    }

    public Task SetAuthenticatorKeyAsync(AspNetCoreIdentityUser user, string key, CancellationToken cancellationToken) =>
        SetTokenAsync(user, AuthenticatorStoreLoginProvider, AuthenticatorKeyTokenName, key, cancellationToken);

    public Task<string?> GetAuthenticatorKeyAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken) =>
        GetTokenAsync(user, AuthenticatorStoreLoginProvider, AuthenticatorKeyTokenName, cancellationToken);

    public async Task ReplaceCodesAsync(AspNetCoreIdentityUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodes);
        var codes = recoveryCodes.Take(MaximumMaterializedRelationshipEntries + 1).ToArray();
        EnsureRelationshipMaterializationLimit(codes.Length, "recovery-code input");
        await SetTokenAsync(user, RecoveryCodeTokenProvider, RecoveryCodeTokenName, string.Join(';', codes), cancellationToken);
    }

    public async Task<bool> RedeemCodeAsync(AspNetCoreIdentityUser user, string code, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        _ = await CountCodesAsync(user, cancellationToken);
        var result = await relationships.RedeemRecoveryCodeAsync(
            user.TenantId,
            user.Id,
            RequireRevision(user),
            RecoveryCodeTokenProvider,
            RecoveryCodeTokenName,
            code,
            cancellationToken);
        if (result.Succeeded)
        {
            ApplyRevisionStamp(user, result);
            return true;
        }
        return false;
    }

    public async Task<int> CountCodesAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        var raw = await GetTokenAsync(user, RecoveryCodeTokenProvider, RecoveryCodeTokenName, cancellationToken);
        var count = ParseRecoveryCodes(raw).Count;
        EnsureRelationshipMaterializationLimit(count, "persisted recovery codes");
        return count;
    }

    private async Task<IdentityResult> SaveFrameworkUserAsync(AspNetCoreIdentityUser user, long expectedRevision, bool createOnly, CancellationToken cancellationToken)
    {
        // The callback is invoked while the aggregate coordinator's transaction is open. This keeps
        // framework state, reservations, the revision, and the mutation receipt in one atomic write.
        var existing = await FindEntityAsync(user.TenantId, user.Id, forWrite: false, cancellationToken);
        var record = (existing is null
                ? new UserRecord(user.Id, user.TenantId, user.UserName ?? string.Empty, user.Email, user.DisplayName,
                    UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal))
                : ToUserRecord(existing)) with
        {
            UserName = user.UserName ?? string.Empty,
            Email = user.Email,
            DisplayName = user.DisplayName
        };
        var result = await aggregates.SaveUserAsync(
            record,
            createOnly ? 0 : expectedRevision,
            emailPolicy.RequireUniqueEmail,
            cancellationToken,
            Guid.NewGuid().ToString("N"),
            entity => ApplyFrameworkState(entity, user));
        if (!result.WriteResult.Succeeded)
            return ToIdentityResult(user, result);

        user.ConcurrencyStamp = IdentityEntityFrameworkRevisionCodec.FromUser(
            user.TenantId,
            user.Id,
            result.WriteResult.Version ?? throw new InvalidOperationException("The Identity aggregate did not return a revision."));
        return IdentityResult.Success;
    }

    private async Task<int> MutateLockoutAsync(AspNetCoreIdentityUser user, Func<AspNetCoreIdentityUser, int> mutate, CancellationToken cancellationToken)
    {
        EnsureUserScope(user);
        if (!IdentityEntityFrameworkRevisionCodec.TryGetUserVersion(user.ConcurrencyStamp, user.TenantId, user.Id, out var expected))
        {
            // UserManager stages lockout defaults before the first CreateAsync call, when the
            // user has no persistence row yet. Preserve that initialization, but never mutate a
            // malformed revision on an existing row.
            if (await FindEntityAsync(user.TenantId, user.Id, forWrite: false, cancellationToken) is null)
                return mutate(user);
            throw new InvalidOperationException("The requested user has no valid EF revision stamp for a lockout mutation.");
        }
        for (var attempt = 0; attempt < LockoutTransitionMaxAttempts; attempt++)
        {
            var candidate = attempt == 0 ? CloneUser(user) : await FindByIdAsync(user.Id, cancellationToken)
                ?? throw new InvalidOperationException("The requested user does not exist in the current persistence scope.");
            if (!IdentityEntityFrameworkRevisionCodec.TryGetUserVersion(candidate.ConcurrencyStamp, user.TenantId, user.Id, out expected))
                throw new InvalidOperationException("The requested user has no valid EF revision stamp.");
            var value = mutate(candidate);
            var result = await SaveFrameworkUserAsync(candidate, expected, false, cancellationToken);
            if (result.Succeeded)
            { CopyLockoutState(candidate, user); return value; }
            if (!result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
                throw new InvalidOperationException("The EF Identity lockout transition failed.");
        }
        throw new InvalidOperationException("EF Identity lockout transition exceeded the bounded retry limit.");
    }

    private async Task WriteRelationshipAsync(
        AspNetCoreIdentityUser user,
        IReadOnlyCollection<UserClaimEntity> additions,
        IReadOnlyCollection<(string ClaimType, string? ClaimValue)> removals,
        CancellationToken cancellationToken)
    {
        var expected = RequireRevision(user);
        EfIdentityWriteResult result;
        if (additions.Count != 0 && removals.Count == 0)
        {
            result = await relationships.AddUserClaimsAsync(user.TenantId, user.Id, expected, additions, cancellationToken);
        }
        else if (additions.Count == 0)
        {
            result = await relationships.RemoveUserClaimsAsync(user.TenantId, user.Id, expected, removals, cancellationToken);
        }
        else
        {
            var replacement = additions.Single();
            var removed = removals.Single();
            result = await relationships.ReplaceUserClaimAsync(user.TenantId, user.Id, expected, removed.ClaimType, removed.ClaimValue, replacement, cancellationToken);
        }
        ApplyRevisionStamp(user, result);
        EnsureRelationshipSucceeded(result, "user-claim mutation");
    }

    private async Task<RoleEntity?> FindRoleAsync(string tenantId, string name, CancellationToken cancellationToken) =>
        await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to find the ASP.NET Core Identity role for the user operation.",
            () => db.Roles.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantLookupKey == TenantKey(tenantId) && x.NormalizedNameKey == RoleKey(tenantId, name),
                cancellationToken));

    private async Task<UserEntity?> FindEntityAsync(string tenantId, string userId, bool forWrite, CancellationToken cancellationToken)
    {
        return await EfIdentityStoreSupport.ReadAsync(
            db,
            "Unable to find the ASP.NET Core Identity user.",
            async () =>
            {
                var query = db.Users.Where(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, userId) && x.TenantLookupKey == TenantKey(tenantId));
                return forWrite ? await query.SingleOrDefaultAsync(cancellationToken) : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            });
    }

    private static void ApplyFrameworkState(UserEntity entity, AspNetCoreIdentityUser user)
    {
        entity.NormalizedUserName = user.NormalizedUserName ?? (string.IsNullOrWhiteSpace(user.UserName) ? null : EfIdentityStoreSupport.Normalize(user.UserName));
        entity.NormalizedUserNameKey = string.IsNullOrWhiteSpace(entity.NormalizedUserName) ? null : UserKey(user.TenantId, entity.NormalizedUserName);
        entity.NormalizedEmail = user.NormalizedEmail ?? (string.IsNullOrWhiteSpace(user.Email) ? null : EfIdentityStoreSupport.Normalize(user.Email));
        entity.NormalizedEmailKey = string.IsNullOrWhiteSpace(entity.NormalizedEmail) ? null : UserKey(user.TenantId, entity.NormalizedEmail);
        entity.EmailConfirmed = user.EmailConfirmed;
        entity.PasswordHash = user.PasswordHash;
        entity.SecurityStamp = user.SecurityStamp;
        entity.ConcurrencyStamp = IdentityEntityFrameworkRevisionCodec.FromUser(user.TenantId, user.Id, entity.Revision);
        entity.PhoneNumber = user.PhoneNumber;
        entity.PhoneNumberConfirmed = user.PhoneNumberConfirmed;
        entity.TwoFactorEnabled = user.TwoFactorEnabled;
        entity.LockoutEnd = user.LockoutEnd;
        entity.LockoutEnabled = user.LockoutEnabled;
        entity.AccessFailedCount = user.AccessFailedCount;
    }

    private static AspNetCoreIdentityUser ToFrameworkUser(UserEntity entity) => new()
    {
        Id = entity.UserId,
        TenantId = entity.TenantId,
        UserName = entity.UserName,
        NormalizedUserName = entity.NormalizedUserName,
        Email = entity.Email,
        NormalizedEmail = entity.NormalizedEmail,
        DisplayName = entity.DisplayName,
        EmailConfirmed = entity.EmailConfirmed,
        PasswordHash = entity.PasswordHash,
        SecurityStamp = entity.SecurityStamp,
        ConcurrencyStamp = IdentityEntityFrameworkRevisionCodec.FromUser(entity.TenantId, entity.UserId, entity.Revision),
        PhoneNumber = entity.PhoneNumber,
        PhoneNumberConfirmed = entity.PhoneNumberConfirmed,
        TwoFactorEnabled = entity.TwoFactorEnabled,
        LockoutEnd = entity.LockoutEnd,
        LockoutEnabled = entity.LockoutEnabled,
        AccessFailedCount = entity.AccessFailedCount
    };

    private static UserRecord ToUserRecord(UserEntity entity) => new(
        entity.UserId, entity.TenantId, entity.UserName, entity.Email, entity.DisplayName, (UserStatus)entity.Status, (ResourceOwnership)entity.Ownership,
        EfIdentityStoreSupport.DeserializeSet(entity.RoleIdsJson), EfIdentityStoreSupport.DeserializeSet(entity.DirectPermissionsJson));

    private static AspNetCoreIdentityUser CloneUser(AspNetCoreIdentityUser source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        UserName = source.UserName,
        NormalizedUserName = source.NormalizedUserName,
        Email = source.Email,
        NormalizedEmail = source.NormalizedEmail,
        EmailConfirmed = source.EmailConfirmed,
        PasswordHash = source.PasswordHash,
        SecurityStamp = source.SecurityStamp,
        ConcurrencyStamp = source.ConcurrencyStamp,
        PhoneNumber = source.PhoneNumber,
        PhoneNumberConfirmed = source.PhoneNumberConfirmed,
        TwoFactorEnabled = source.TwoFactorEnabled,
        LockoutEnd = source.LockoutEnd,
        LockoutEnabled = source.LockoutEnabled,
        AccessFailedCount = source.AccessFailedCount,
        DisplayName = source.DisplayName
    };

    private static IReadOnlySet<string> ParseRecoveryCodes(string? raw) =>
        (raw ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

    private void EnsureUserScope(AspNetCoreIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureTenant(user.TenantId);
    }

    private void EnsureTenant(string tenantId) => accessContextAccessor.Current.EnsureTenantScope(tenantId);

    private string CurrentTenantId() => accessContextAccessor.Current.Scope?.Value ?? PersistenceScope.DefaultValue;
    private static string TenantKey(string tenantId) => EfIdentityStoreSupport.TenantLookup(tenantId);
    private static string UserKey(string tenantId, string? value) => EfIdentityStoreSupport.Lookup(tenantId, value);
    private static string RoleKey(string tenantId, string value) => EfIdentityStoreSupport.Lookup(tenantId, value);
    private static long RequireRevision(AspNetCoreIdentityUser user) =>
        IdentityEntityFrameworkRevisionCodec.TryGetUserVersion(user.ConcurrencyStamp, user.TenantId, user.Id, out var version)
            ? version
            : throw new InvalidOperationException("The requested user has no valid EF revision stamp.");

    private static void ApplyRevisionStamp(AspNetCoreIdentityUser user, EfIdentityWriteResult result)
    {
        if (result.Succeeded && result.Version is { } version)
            user.ConcurrencyStamp = IdentityEntityFrameworkRevisionCodec.FromUser(user.TenantId, user.Id, version);
    }

    private static void EnsureRelationshipSucceeded(EfIdentityWriteResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"The EF Identity {operation} returned {result.Status}: {result.Message}");
    }

    private static void EnsureRelationshipMaterializationLimit(int count, string subject)
    {
        if (count > MaximumMaterializedRelationshipEntries)
            throw new InvalidOperationException($"The EF Identity {subject} exceed the {MaximumMaterializedRelationshipEntries}-entry materialization limit.");
    }

    private static IdentityResult ConcurrencyFailure() => IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
    private static IdentityResult ToIdentityResult(EfIdentityWriteResult result) => result.Status switch
    {
        EfIdentityWriteStatus.Inserted or EfIdentityWriteStatus.Updated or EfIdentityWriteStatus.Deleted => IdentityResult.Success,
        EfIdentityWriteStatus.Conflict => ConcurrencyFailure(),
        _ => IdentityResult.Failed(new IdentityError { Code = $"EfIdentity{result.Status}", Description = result.Message })
    };

    private static IdentityResult ToIdentityResult(AspNetCoreIdentityUser user, EfIdentityAuthorityWriteResult result) => result.Conflict switch
    {
        EfIdentityAuthorityConflict.UserName => IdentityResult.Failed(new IdentityErrorDescriber().DuplicateUserName(user.NormalizedUserName ?? user.UserName ?? user.Id)),
        EfIdentityAuthorityConflict.Email => IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(user.NormalizedEmail ?? user.Email ?? user.Id)),
        _ => ToIdentityResult(result.WriteResult)
    };
    private static void CopyLockoutState(AspNetCoreIdentityUser source, AspNetCoreIdentityUser target) { target.AccessFailedCount = source.AccessFailedCount; target.LockoutEnd = source.LockoutEnd; target.LockoutEnabled = source.LockoutEnabled; target.ConcurrencyStamp = source.ConcurrencyStamp; }
}
