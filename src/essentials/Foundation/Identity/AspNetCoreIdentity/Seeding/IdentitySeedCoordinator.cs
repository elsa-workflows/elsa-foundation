using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;

/// <summary>
/// Provider-neutral orchestration for the initial ASP.NET Core Identity administrator. Concrete persistence
/// providers own only their schema/store initialization; account, role and permission convergence lives here.
/// </summary>
public sealed class IdentitySeedCoordinator(
    IOptions<AspNetCoreIdentityOptions> identityOptions,
    UserManager<AspNetCoreIdentityUser> userManager,
    IUserStore userStore,
    IRoleStore roleStore,
    ITenantMembershipStore membershipStore,
    IPermissionCatalog permissionCatalog,
    TimeProvider? timeProvider = null)
{
    private const int MaxSeedConvergenceAttempts = 8;

    /// <summary>The pause before a re-read grows by this much per failed attempt, so nodes that lost a race together drift apart.</summary>
    private static readonly TimeSpan ConvergenceBackoffStep = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// The all-access permission ("*") that Elsa endpoints secured with permission policies require.
    /// Kept as a literal here without taking an API dependency.
    /// </summary>
    public const string AllAccessPermission = "*";

    public async Task<SeedResult> ValidateSeedOptionsAsync(IdentitySeedOptions seed, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(seed.UserName))
            return new MissingUserName();

        if (string.IsNullOrWhiteSpace(seed.Password))
            return new MissingPassword();

        var tenantId = identityOptions.Value.DefaultTenantId;
        var candidate = CreateAdminUser(seed, tenantId);
        var errors = new List<IdentityError>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, candidate, seed.Password);
            if (!result.Succeeded)
                errors.AddRange(result.Errors);
        }

        return errors.Count == 0
            ? new Valid()
            : new PasswordPolicyRejected(errors.Select(x => x.Code).Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<SeedResult> EnsureSeededAsync(IdentitySeedOptions seed, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateSeedOptionsAsync(seed, cancellationToken);
        if (validation is not Valid)
            return validation;

        var tenantId = identityOptions.Value.DefaultTenantId;
        var role = await EnsureAdminRoleAsync(seed, cancellationToken);
        var createdUser = await EnsureAdminUserAsync(seed, tenantId, role.Id, cancellationToken);

        return createdUser switch
        {
            Created => createdUser,
            ConvergedAfterRace => createdUser,
            PasswordPolicyRejected rejected => rejected,
            _ => new AlreadyConverged()
        };
    }

    public async Task<RoleRecord> EnsureAdminRoleAsync(IdentitySeedOptions seed, CancellationToken cancellationToken = default)
    {
        var tenantId = identityOptions.Value.DefaultTenantId;
        var expectedPermissions = permissionCatalog
            .List()
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        expectedPermissions.Add(AllAccessPermission);

        return await ConvergeAsync("the administrator role", async () =>
        {
            var existing = (await roleStore.ListAsync(tenantId, cancellationToken))
                .FirstOrDefault(x => string.Equals(x.Name, seed.RoleName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                var role = new RoleRecord(
                    SeedDocumentId("role", tenantId, seed.RoleName),
                    tenantId,
                    seed.RoleName,
                    "Administrator",
                    expectedPermissions,
                    System: true);
                var result = await RevisionRoleStore.SaveWithRevisionAsync(role, expectedRevision: null, cancellationToken);
                return result.Status is IamRevisionSaveStatus.Saved ? Attempt<RoleRecord>.Done(role) : default;
            }

            var revisioned = await RevisionRoleStore.FindWithRevisionAsync(tenantId, existing.Id, cancellationToken);
            if (revisioned is null)
                return default;

            existing = revisioned.Record;
            if (expectedPermissions.All(existing.Permissions.Contains) && existing.System)
                return Attempt<RoleRecord>.Done(existing);

            var mergedPermissions = existing.Permissions
                .Concat(expectedPermissions)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var converged = existing with { Permissions = mergedPermissions, System = true };
            var save = await RevisionRoleStore.SaveWithRevisionAsync(converged, revisioned.Revision, cancellationToken);
            return save.Status is IamRevisionSaveStatus.Saved ? Attempt<RoleRecord>.Done(converged) : default;
        }, cancellationToken);
    }

    /// <summary>
    /// Materializes the framework <c>UserRole</c> relationship for an already-seeded user so
    /// <see cref="UserManager{TUser}"/> role queries observe the converged membership. Two nodes seeding the
    /// same database race on this write, so a lost race (an <see cref="IdentityRevisionConflictException"/> or a
    /// <c>ConcurrencyFailure</c> result) re-reads the user and tries again, and a membership that is already present
    /// (including <c>UserAlreadyInRole</c>) counts as converged. Any other failure propagates unchanged.
    /// </summary>
    public async Task EnsureFrameworkRoleMembershipAsync(string userName, string roleName, CancellationToken cancellationToken = default)
    {
        await ConvergeAsync("the administrator framework role membership", async () =>
        {
            var user = await userManager.FindByNameAsync(userName)
                       ?? throw new InvalidOperationException("The EF Identity administrator was not available after seeding.");
            if (await userManager.IsInRoleAsync(user, roleName))
                return Settled;

            var result = await userManager.AddToRoleAsync(user, roleName);
            if (result.Succeeded || result.Errors.Any(x => x.Code == nameof(IdentityErrorDescriber.UserAlreadyInRole)))
                return Settled;
            if (result.Errors.Any(x => x.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
                throw new IdentityRevisionConflictException("Adding the administrator to its role returned ConcurrencyFailure.");

            throw new InvalidOperationException("Failed to materialize the EF Identity administrator role membership: " + string.Join("; ", result.Errors.Select(x => x.Code)));
        }, cancellationToken);
    }

    private Task<SeedResult> EnsureAdminUserAsync(
        IdentitySeedOptions seed,
        string tenantId,
        string roleId,
        CancellationToken cancellationToken)
    {
        IdentityRevisionConflictException? lostCreateRace = null;
        return ConvergeAsync("the administrator account", async () =>
        {
            var existing = await userManager.FindByNameAsync(seed.UserName);
            SeedResult outcome = lostCreateRace is null ? new AlreadyConverged() : new ConvergedAfterRace();
            if (existing is null)
            {
                // A peer won the create but left no row this re-read can see: there is nothing to converge on.
                if (lostCreateRace is not null)
                    throw new InvalidOperationException("The administrator create lost a race, but no administrator was visible on the re-read.", lostCreateRace);

                var user = CreateAdminUser(seed, tenantId);
                try
                {
                    var result = await userManager.CreateAsync(user, seed.Password);
                    if (result.Succeeded)
                    {
                        existing = user;
                        outcome = new Created();
                    }
                    else
                    {
                        // A duplicate name that shows up on the re-read is a peer's create; otherwise the create was refused.
                        existing = await userManager.FindByNameAsync(seed.UserName);
                        if (existing is null)
                            return Attempt<SeedResult>.Done(new PasswordPolicyRejected(result.Errors.Select(x => x.Code).ToArray()));

                        outcome = new ConvergedAfterRace();
                    }
                }
                catch (IdentityRevisionConflictException exception)
                {
                    // A peer that created the administrator after UserManager's existence check makes the store throw
                    // (its lockout write finds a row this user object has no revision for) instead of returning a
                    // failure. Re-read on the next attempt and converge on the peer's row.
                    lostCreateRace = exception;
                    return default;
                }
            }

            await EnsureUserRoleAsync(tenantId, existing.Id, roleId, cancellationToken);
            await EnsureMembershipAsync(tenantId, existing.Id, roleId, cancellationToken);
            return Attempt<SeedResult>.Done(outcome);
        }, cancellationToken);
    }

    private async Task EnsureUserRoleAsync(
        string tenantId,
        string userId,
        string roleId,
        CancellationToken cancellationToken)
    {
        await ConvergeAsync("the administrator user role", async () =>
        {
            var revisioned = await RevisionUserStore.FindWithRevisionAsync(tenantId, userId, cancellationToken);
            if (revisioned is null)
                return Settled;

            var record = revisioned.Record;
            if (record.RoleIds.Contains(roleId))
                return Settled;

            var roleIds = record.RoleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            roleIds.Add(roleId);
            var directPermissions = record.DirectPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var converged = record with
            {
                Ownership = ResourceOwnership.Foundation,
                RoleIds = roleIds,
                DirectPermissions = directPermissions
            };
            var result = await RevisionUserStore.SaveWithRevisionAsync(converged, revisioned.Revision, cancellationToken);
            return result.Status is IamRevisionSaveStatus.Saved ? Settled : default;
        }, cancellationToken);
    }

    private async Task EnsureMembershipAsync(
        string tenantId,
        string userId,
        string roleId,
        CancellationToken cancellationToken)
    {
        await ConvergeAsync("the administrator tenant membership", async () =>
        {
            var revisioned = await RevisionMembershipStore.FindWithRevisionAsync(tenantId, userId, cancellationToken);
            if (revisioned is null)
            {
                var membership = new TenantMembershipRecord(
                    tenantId,
                    userId,
                    TenantMembershipStatus.Active,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { roleId },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                var create = await RevisionMembershipStore.SaveWithRevisionAsync(membership, expectedRevision: null, cancellationToken);
                return create.Status is IamRevisionSaveStatus.Saved ? Settled : default;
            }

            var existing = revisioned.Record;
            if (existing.Status == TenantMembershipStatus.Active && existing.RoleIds.Contains(roleId))
                return Settled;

            var roleIds = existing.RoleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            roleIds.Add(roleId);
            var directPermissions = existing.DirectPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = await RevisionMembershipStore.SaveWithRevisionAsync(existing with
            {
                Status = TenantMembershipStatus.Active,
                RoleIds = roleIds,
                DirectPermissions = directPermissions
            }, revisioned.Revision, cancellationToken);
            return result.Status is IamRevisionSaveStatus.Saved ? Settled : default;
        }, cancellationToken);
    }

    /// <summary>The outcome of one convergence attempt: <c>default</c> means a conditional-write conflict, so try again.</summary>
    private readonly record struct Attempt<T>(bool Converged, T? Value)
    {
        public static Attempt<T> Done(T value) => new(true, value);
    }

    private static Attempt<bool> Settled => Attempt<bool>.Done(true);

    /// <summary>
    /// The one bounded convergence loop every seeding step uses. <paramref name="attemptConvergence"/> returns
    /// <see cref="Attempt{T}.Done"/> once the state has converged and <c>default</c> after a conditional-write conflict;
    /// an <see cref="IdentityRevisionConflictException"/> counts as a conflict too. A growing pause precedes each re-read
    /// so concurrent nodes do not retry in lockstep. Running out of attempts reports the last such exception, when there
    /// was one, as the cause.
    /// </summary>
    private async Task<T> ConvergeAsync<T>(string subject, Func<Task<Attempt<T>>> attemptConvergence, CancellationToken cancellationToken)
    {
        IdentityRevisionConflictException? lastConflict = null;
        for (var attempt = 0; attempt < MaxSeedConvergenceAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 0)
                await Task.Delay(ConvergenceBackoffStep * attempt, timeProvider ?? TimeProvider.System, cancellationToken);

            try
            {
                var outcome = await attemptConvergence();
                if (outcome.Converged)
                    return outcome.Value!;
            }
            catch (IdentityRevisionConflictException exception)
            {
                lastConflict = exception;
            }
        }

        throw new InvalidOperationException($"Identity seeding could not converge {subject} after repeated conditional-write conflicts.", lastConflict);
    }

    private static string SeedDocumentId(string kind, string tenantId, string logicalName)
    {
        var input = $"{kind}\n{tenantId}\n{logicalName.ToUpperInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"seed-{kind}-{Convert.ToHexString(hash).ToLowerInvariant()[..24]}";
    }

    private static AspNetCoreIdentityUser CreateAdminUser(IdentitySeedOptions seed, string tenantId) => new()
    {
        Id = SeedDocumentId("user", tenantId, seed.UserName),
        UserName = seed.UserName,
        Email = seed.Email,
        TenantId = tenantId,
        DisplayName = "Administrator",
        EmailConfirmed = !string.IsNullOrWhiteSpace(seed.Email),
        LockoutEnabled = true
    };

    private IRevisionAwareUserStore RevisionUserStore => userStore as IRevisionAwareUserStore
        ?? throw new InvalidOperationException("Identity seeding requires a revision-aware user store.");

    private IRevisionAwareRoleStore RevisionRoleStore => roleStore as IRevisionAwareRoleStore
        ?? throw new InvalidOperationException("Identity seeding requires a revision-aware role store.");

    private IRevisionAwareTenantMembershipStore RevisionMembershipStore => membershipStore as IRevisionAwareTenantMembershipStore
        ?? throw new InvalidOperationException("Identity seeding requires a revision-aware tenant membership store.");

    public abstract record SeedResult;

    public sealed record Valid : SeedResult;

    public sealed record MissingUserName : SeedResult;

    public sealed record MissingPassword : SeedResult;

    public sealed record PasswordPolicyRejected(IReadOnlyCollection<string> Errors) : SeedResult;

    public sealed record Created : SeedResult;

    public sealed record AlreadyConverged : SeedResult;

    public sealed record ConvergedAfterRace : SeedResult;
}
