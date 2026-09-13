using System.Security.Claims;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Authentication;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests;

public sealed class EfCoreIdentityFrameworkContractTests
{
    [Fact]
    public async Task Claims_logins_roles_and_role_claims_round_trip_through_authority_coordinators()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var role = await scenario.CreateRoleAsync("Operators");
        var user = await scenario.CreateUserAsync("Alice", "alice@example.test");
        var claim = new Claim("department", "operations");
        var replacement = new Claim("department", "platform");

        Assert.True((await scenario.Users.AddClaimAsync(user, claim)).Succeeded);
        Assert.Contains(await scenario.Users.GetClaimsAsync(user), x => x.Type == claim.Type && x.Value == claim.Value);
        Assert.Contains(await scenario.Users.GetUsersForClaimAsync(claim), x => x.Id == user.Id);
        Assert.True((await scenario.Users.ReplaceClaimAsync(user, claim, replacement)).Succeeded);
        Assert.DoesNotContain(await scenario.Users.GetClaimsAsync(user), x => x.Value == claim.Value);
        Assert.True((await scenario.Users.RemoveClaimAsync(user, replacement)).Succeeded);

        var login = new UserLoginInfo("oidc", "subject-1", "OIDC");
        Assert.True((await scenario.Users.AddLoginAsync(user, login)).Succeeded);
        var persistedLogin = Assert.Single(await scenario.Users.GetLoginsAsync(user));
        Assert.Equal(login.ProviderKey, persistedLogin.ProviderKey);
        Assert.Equal(login.ProviderDisplayName, persistedLogin.ProviderDisplayName);
        Assert.Equal(user.Id, (await scenario.Users.FindByLoginAsync(login.LoginProvider, login.ProviderKey))?.Id);
        Assert.True((await scenario.Users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey)).Succeeded);

        Assert.True((await scenario.Users.AddToRoleAsync(user, role.Name!)).Succeeded);
        Assert.True(await scenario.Users.IsInRoleAsync(user, role.Name!));
        Assert.Contains(role.Name, await scenario.Users.GetRolesAsync(user));
        Assert.Contains(user.Id, (await scenario.Users.GetUsersInRoleAsync(role.Name!)).Select(x => x.Id));
        Assert.True((await scenario.Users.RemoveFromRoleAsync(user, role.Name!)).Succeeded);

        // Relationship coordination advances the authoritative role revision as well as the user
        // revision. Refresh the framework role before its next CAS-protected mutation.
        role = await scenario.Roles.FindByIdAsync(role.Id) ?? throw new InvalidOperationException("The role disappeared during relationship coordination.");
        var roleClaim = new Claim("scope", "write");
        var roleClaimReplacement = new Claim("scope", "admin");
        Assert.True((await scenario.Roles.AddClaimAsync(role, roleClaim)).Succeeded);
        Assert.Contains(await scenario.Roles.GetClaimsAsync(role), x => x.Value == roleClaim.Value);
        var roleClaimStore = scenario.Services.GetRequiredService<IRoleClaimStore<IdentityRole>>();
        await ((EfCoreIdentityRoleStore)roleClaimStore).ReplaceClaimAsync(role, roleClaim, roleClaimReplacement);
        await roleClaimStore.RemoveClaimAsync(role, roleClaimReplacement);
        Assert.Empty(await scenario.Roles.GetClaimsAsync(role));
    }

    [Fact]
    public async Task Login_display_names_round_trip_null_and_unpaired_surrogates_after_a_context_reopen()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-identity-login-display-{Guid.NewGuid():N}.db");
        await using var first = await EfCoreIdentityScenario.CreateAsync(databasePath: databasePath);
        var user = await first.CreateUserAsync("DisplayNames");
        Assert.True((await first.Users.AddLoginAsync(user, new UserLoginInfo("oidc", "null-display", null))).Succeeded);
        Assert.True((await first.Users.AddLoginAsync(user, new UserLoginInfo("oidc", "surrogate-display", "OIDC\ud800"))).Succeeded);
        await first.Services.GetRequiredService<IdentityIamDbContext>().Database.CloseConnectionAsync();

        await using var reopened = await EfCoreIdentityScenario.CreateAsync(databasePath: databasePath, ensureSchema: false);
        var loadedUser = await reopened.Users.FindByIdAsync(user.Id);
        var logins = (await reopened.Users.GetLoginsAsync(Assert.IsType<AspNetCoreIdentityUser>(loadedUser)))
            .OrderBy(login => login.ProviderKey, StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            logins,
            login =>
            {
                Assert.Equal("null-display", login.ProviderKey);
                Assert.Null(login.ProviderDisplayName);
            },
            login =>
            {
                Assert.Equal("surrogate-display", login.ProviderKey);
                Assert.Equal("OIDC\ud800", login.ProviderDisplayName);
            });
    }

    [Fact]
    public async Task Provider_neutral_external_identity_update_preserves_framework_display_name()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("ProviderNeutralDisplay");
        var login = new UserLoginInfo("oidc", "subject-preserve", "Friendly OIDC");
        Assert.True((await scenario.Users.AddLoginAsync(user, login)).Succeeded);

        var externalIdentities = scenario.Services.GetRequiredService<IExternalIdentityStore>();
        var record = Assert.IsType<ExternalIdentityRecord>(
            await externalIdentities.FindBySubjectAsync(scenario.TenantId, login.LoginProvider, login.ProviderKey));
        await externalIdentities.SaveAsync(record with { LastSeenAt = DateTimeOffset.UtcNow });

        var persisted = Assert.Single(await scenario.Users.GetLoginsAsync(user));
        Assert.Equal(login.ProviderDisplayName, persisted.ProviderDisplayName);
    }

    [Fact]
    public async Task Different_framework_display_names_are_distinct_external_login_mutations()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("DisplayReplayIdentity");
        var store = scenario.Services.GetRequiredService<IUserLoginStore<AspNetCoreIdentityUser>>();

        await store.AddLoginAsync(user, new UserLoginInfo("oidc", "subject-replay", "First name"), CancellationToken.None);
        await store.AddLoginAsync(user, new UserLoginInfo("oidc", "subject-replay", "Second name"), CancellationToken.None);

        var persisted = Assert.Single(await store.GetLoginsAsync(user, CancellationToken.None));
        Assert.Equal("Second name", persisted.ProviderDisplayName);
    }

    [Fact]
    public async Task Role_membership_reads_use_canonical_role_ids_and_stable_role_order()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var roleZ = new IdentityRole { Id = "Role-Z", Name = "Role Z" };
        var roleA = new IdentityRole { Id = "role-A", Name = "Role A" };
        Assert.True((await scenario.Roles.CreateAsync(roleZ)).Succeeded);
        Assert.True((await scenario.Roles.CreateAsync(roleA)).Succeeded);
        var user = new AspNetCoreIdentityUser
        {
            Id = "User-A",
            TenantId = scenario.TenantId,
            UserName = "MembershipReader",
            Email = "membership-reader@example.test"
        };
        var userCreateResult = await scenario.Users.CreateAsync(user, "Correct Horse1!");
        Assert.True(userCreateResult.Succeeded, string.Join("; ", userCreateResult.Errors.Select(error => error.Description)));
        var userZ = new AspNetCoreIdentityUser
        {
            Id = "user-Z",
            TenantId = scenario.TenantId,
            UserName = "MembershipReaderZ",
            Email = "membership-reader-z@example.test"
        };
        Assert.True((await scenario.Users.CreateAsync(userZ, "Correct Horse1!")).Succeeded);

        var membershipClaim = new Claim("membership", "reader");
        Assert.True((await scenario.Users.AddClaimAsync(user, membershipClaim)).Succeeded);
        Assert.True((await scenario.Users.AddClaimAsync(userZ, membershipClaim)).Succeeded);

        var context = scenario.Services.GetRequiredService<IdentityIamDbContext>();
        var relationships = scenario.Services.GetRequiredService<EfIdentityAuthorityRelationshipCoordinator>();
        var revision = await context.Users.AsNoTracking()
            .Where(x => x.TenantId == scenario.TenantId && x.UserId == user.Id)
            .Select(x => x.Revision)
            .SingleAsync();
        var first = await relationships.AddUserRoleAsync(
            scenario.TenantId,
            user.Id,
            roleZ.Id,
            revision,
            new UserRoleEntity());
        revision = Assert.IsType<long>(first.Version);
        var second = await relationships.AddUserRoleAsync(
            scenario.TenantId,
            user.Id,
            roleA.Id,
            revision,
            new UserRoleEntity());
        revision = Assert.IsType<long>(second.Version);

        // The deterministic link ID is case-insensitive, so this updates the existing link while
        // retaining a different raw role ID spelling in the relationship row.
        var caseVariant = await relationships.AddUserRoleAsync(
            scenario.TenantId,
            user.Id,
            roleZ.Id.ToLowerInvariant(),
            revision,
            new UserRoleEntity());
        Assert.Equal(EfIdentityWriteStatus.Updated, caseVariant.Status);

        Assert.Equal([user.Id, userZ.Id],
            (await scenario.Users.GetUsersForClaimAsync(membershipClaim)).Select(candidate => candidate.Id));
        Assert.True((await scenario.Users.AddToRoleAsync(userZ, roleZ.Name!)).Succeeded);
        Assert.Equal([user.Id, userZ.Id],
            (await scenario.Users.GetUsersInRoleAsync(roleZ.Name!)).Select(candidate => candidate.Id));

        Assert.Equal([roleA.Name, roleZ.Name], await scenario.Users.GetRolesAsync(user));
        Assert.Contains(user.Id, (await scenario.Users.GetUsersInRoleAsync(roleZ.Name!)).Select(x => x.Id));
    }

    [Fact]
    public async Task Tokens_authenticator_and_recovery_codes_follow_identity_store_conventions()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("TokenUser");

        Assert.True((await scenario.Users.SetAuthenticationTokenAsync(user, "oidc", "access", "secret")).Succeeded);
        Assert.Equal("secret", await scenario.Users.GetAuthenticationTokenAsync(user, "oidc", "access"));
        Assert.True((await scenario.Users.RemoveAuthenticationTokenAsync(user, "oidc", "access")).Succeeded);
        Assert.Null(await scenario.Users.GetAuthenticationTokenAsync(user, "oidc", "access"));

        Assert.True((await scenario.Users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(await scenario.Users.GetAuthenticatorKeyAsync(user)));

        var recoveryCodes = (await scenario.Users.GenerateNewTwoFactorRecoveryCodesAsync(user, 3))!.ToArray();
        Assert.Equal(3, await scenario.Users.CountRecoveryCodesAsync(user));
        Assert.True((await scenario.Users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCodes[0])).Succeeded);
        Assert.False((await scenario.Users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCodes[0])).Succeeded);
        Assert.Equal(2, await scenario.Users.CountRecoveryCodesAsync(user));

        var store = scenario.Services.GetRequiredService<IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceCodesAsync(user, Enumerable.Range(0, 513).Select(x => $"code-{x}"), CancellationToken.None));
    }

    [Fact]
    public async Task Recovery_code_count_and_redemption_treat_codes_as_a_set()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("MalformedRecoveryCodes");
        var store = scenario.Services.GetRequiredService<IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>>();

        await store.ReplaceCodesAsync(user, ["alpha", "", "alpha", "beta", ""], CancellationToken.None);

        Assert.Equal(2, await scenario.Users.CountRecoveryCodesAsync(user));
        Assert.True((await scenario.Users.RedeemTwoFactorRecoveryCodeAsync(user, "alpha")).Succeeded);
        Assert.False((await scenario.Users.RedeemTwoFactorRecoveryCodeAsync(user, "alpha")).Succeeded);
        Assert.True((await scenario.Users.RedeemTwoFactorRecoveryCodeAsync(user, "beta")).Succeeded);
        Assert.Equal(0, await scenario.Users.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task Lockout_mutations_persist_and_stale_framework_updates_map_to_concurrency_failure()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("LockoutUser");

        Assert.True((await scenario.Users.SetLockoutEnabledAsync(user, true)).Succeeded);
        Assert.True((await scenario.Users.AccessFailedAsync(user)).Succeeded);
        Assert.Equal(1, await scenario.Users.GetAccessFailedCountAsync(user));
        var end = DateTimeOffset.UtcNow.AddMinutes(10);
        Assert.True((await scenario.Users.SetLockoutEndDateAsync(user, end)).Succeeded);
        Assert.Equal(end, await scenario.Users.GetLockoutEndDateAsync(user));
        Assert.True((await scenario.Users.ResetAccessFailedCountAsync(user)).Succeeded);
        Assert.Equal(0, await scenario.Users.GetAccessFailedCountAsync(user));

        var stale = await scenario.Users.FindByIdAsync(user.Id);
        Assert.NotNull(stale);
        Assert.True((await scenario.Users.UpdateAsync(user)).Succeeded);
        stale!.DisplayName = "stale update";
        var result = await scenario.Users.UpdateAsync(stale);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, x => x.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
    }

    [Fact]
    public async Task Invalid_lockout_revision_fails_without_mutating_user_state()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("InvalidLockoutStamp");
        var originalCount = user.AccessFailedCount;
        user.ConcurrencyStamp = "invalid-lockout-stamp";

        var store = scenario.Services.GetRequiredService<IUserLockoutStore<AspNetCoreIdentityUser>>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.IncrementAccessFailedCountAsync(user, CancellationToken.None));

        Assert.Equal(originalCount, user.AccessFailedCount);
        Assert.Equal("invalid-lockout-stamp", user.ConcurrencyStamp);
        var reloaded = await scenario.Users.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(originalCount, reloaded!.AccessFailedCount);
    }

    [Fact]
    public async Task Unique_email_and_role_reservation_conflicts_map_to_identity_errors()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(services =>
            services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true));
        var first = await scenario.CreateUserAsync("First", "same@example.test");
        var duplicate = new AspNetCoreIdentityUser { Id = "second", TenantId = scenario.TenantId, UserName = "Second", Email = first.Email };
        var duplicateResult = await scenario.Users.CreateAsync(duplicate);
        Assert.False(duplicateResult.Succeeded);
        Assert.Contains(duplicateResult.Errors, x => x.Code == nameof(IdentityErrorDescriber.DuplicateEmail));

        var role = await scenario.CreateRoleAsync("Auditors");
        var duplicateRole = new IdentityRole { Id = "role-duplicate", Name = role.Name, NormalizedName = role.NormalizedName };
        var duplicateRoleResult = await scenario.Roles.CreateAsync(duplicateRole);
        Assert.False(duplicateRoleResult.Succeeded);
        Assert.Contains(duplicateRoleResult.Errors, x => x.Code == nameof(IdentityErrorDescriber.DuplicateRoleName));
    }

    [Fact]
    public async Task Duplicate_authority_root_with_a_new_reservation_maps_to_concurrency_not_name_collision()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var existingUser = new AspNetCoreIdentityUser
        {
            Id = "shared-user-id",
            TenantId = scenario.TenantId,
            UserName = "FirstUser"
        };
        Assert.True((await scenario.Users.CreateAsync(existingUser)).Succeeded);

        var duplicateUserRoot = new AspNetCoreIdentityUser
        {
            Id = existingUser.Id,
            TenantId = scenario.TenantId,
            UserName = "SecondUser",
            NormalizedUserName = "SECONDUSER"
        };
        var userStore = scenario.Services.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>();
        var userResult = await userStore.CreateAsync(duplicateUserRoot, CancellationToken.None);
        Assert.False(userResult.Succeeded);
        Assert.Contains(userResult.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.DoesNotContain(userResult.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateUserName));

        var existingRole = new IdentityRole { Id = "shared-role-id", Name = "FirstRole" };
        Assert.True((await scenario.Roles.CreateAsync(existingRole)).Succeeded);
        var duplicateRoleRoot = new IdentityRole { Id = existingRole.Id, Name = "SecondRole", NormalizedName = "SECONDROLE" };
        var roleStore = scenario.Services.GetRequiredService<IRoleStore<IdentityRole>>();
        var roleResult = await roleStore.CreateAsync(duplicateRoleRoot, CancellationToken.None);
        Assert.False(roleResult.Succeeded);
        Assert.Contains(roleResult.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        Assert.DoesNotContain(roleResult.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateRoleName));
    }

    [Fact]
    public async Task Custom_lookup_normalizer_drives_user_and_role_lookup_and_reservations()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(services =>
        {
            services.AddSingleton<ILookupNormalizer, DeliberateLookupNormalizer>();
            services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true);
        });

        var user = await scenario.CreateUserAsync("Alice", "Alice@example.test");
        Assert.Equal("name::alice", user.NormalizedUserName);
        Assert.Equal("email::alice@example.test", user.NormalizedEmail);
        Assert.Equal(user.Id, (await scenario.Users.FindByNameAsync("ALICE"))?.Id);
        Assert.Equal(user.Id, (await scenario.Users.FindByEmailAsync("ALICE@EXAMPLE.TEST"))?.Id);

        var duplicateName = new AspNetCoreIdentityUser
        {
            Id = "duplicate-name",
            TenantId = scenario.TenantId,
            UserName = "ALICE",
            Email = "different@example.test"
        };
        var duplicateNameResult = await scenario.Users.CreateAsync(duplicateName, "Correct Horse1!");
        Assert.Contains(duplicateNameResult.Errors, x => x.Code == nameof(IdentityErrorDescriber.DuplicateUserName));

        var duplicateEmail = new AspNetCoreIdentityUser
        {
            Id = "duplicate-email",
            TenantId = scenario.TenantId,
            UserName = "Bob",
            Email = "ALICE@EXAMPLE.TEST"
        };
        var duplicateEmailResult = await scenario.Users.CreateAsync(duplicateEmail, "Correct Horse1!");
        Assert.Contains(duplicateEmailResult.Errors, x => x.Code == nameof(IdentityErrorDescriber.DuplicateEmail));

        var role = new IdentityRole { Id = "normalized-role", Name = "Operators" };
        Assert.True((await scenario.Roles.CreateAsync(role)).Succeeded);
        Assert.Equal("name::operators", role.NormalizedName);
        Assert.Equal(role.Id, (await scenario.Roles.FindByNameAsync("OPERATORS"))?.Id);

        var duplicateRole = new IdentityRole { Id = "normalized-role-duplicate", Name = "OPERATORS" };
        var duplicateRoleResult = await scenario.Roles.CreateAsync(duplicateRole);
        Assert.Contains(duplicateRoleResult.Errors, x => x.Code == nameof(IdentityErrorDescriber.DuplicateRoleName));
    }

    [Fact]
    public async Task Principal_factory_and_session_invalidator_use_tenant_and_security_stamp_claims()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("SessionUser");
        var factory = scenario.Services.GetRequiredService<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>>();
        var principal = await factory.CreateAsync(user);
        Assert.Equal(scenario.TenantId, principal.FindFirstValue(IdentityClaimTypes.TenantId));
        var stampType = scenario.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
        var stamp = principal.FindFirstValue(stampType);
        Assert.Equal(user.SecurityStamp, stamp);

        var invalidator = scenario.Services.GetRequiredService<IAuthenticationSessionInvalidator>();
        await invalidator.InvalidateAsync(new AuthenticationSessionInvalidationContext(principal));
        var reloaded = await scenario.Users.FindByIdAsync(user.Id);
        Assert.NotEqual(stamp, reloaded?.SecurityStamp);
    }

    [Fact]
    public async Task Framework_query_provider_failures_use_the_stable_persistence_exception_boundary()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(ensureSchema: false);

        var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() =>
            scenario.Users.FindByIdAsync("missing-user"));

        Assert.Contains("Unable to find the ASP.NET Core Identity user.", exception.Message, StringComparison.Ordinal);
        Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(exception.InnerException);
    }

    [Fact]
    public async Task Cookie_with_a_stale_security_stamp_is_rejected_and_signed_out()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("CookieStamp");
        var context = CreateCookieContext(scenario.Services, user.Id, scenario.TenantId, "stale-security-stamp");

        await scenario.Services.GetRequiredService<EfCoreIdentityCookieEvents>().ValidatePrincipal(context);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Cookie_missing_security_stamp_is_rejected_and_signed_out()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("CookieMissingStamp");
        var context = CreateCookieContext(scenario.Services, user.Id, scenario.TenantId, securityStamp: null);

        await scenario.Services.GetRequiredService<EfCoreIdentityCookieEvents>().ValidatePrincipal(context);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Cookie_tenant_conflict_with_an_already_bound_request_scope_is_rejected()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync();
        var user = await scenario.CreateUserAsync("CookieTenant");
        var context = CreateCookieContext(scenario.Services, user.Id, "tenant-b", user.SecurityStamp);

        await scenario.Services.GetRequiredService<EfCoreIdentityCookieEvents>().ValidatePrincipal(context);

        Assert.Null(context.Principal);
    }

    private static CookieValidatePrincipalContext CreateCookieContext(
        IServiceProvider services,
        string userId,
        string tenantId,
        string? securityStamp)
    {
        var stampClaimType = services.GetRequiredService<IOptions<IdentityOptions>>().Value.ClaimsIdentity.SecurityStampClaimType;
        var identity = new ClaimsIdentity(AspNetCoreIdentityDefaults.CookieScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, tenantId));
        if (securityStamp is not null)
            identity.AddClaim(new Claim(stampClaimType, securityStamp));

        var scheme = new AuthenticationScheme(
            AspNetCoreIdentityDefaults.CookieScheme,
            AspNetCoreIdentityDefaults.CookieScheme,
            typeof(CookieAuthenticationHandler));
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AspNetCoreIdentityDefaults.CookieScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), scheme.Name);
        var httpContext = new DefaultHttpContext { RequestServices = services };
        return new CookieValidatePrincipalContext(httpContext, scheme, options, ticket);
    }

    [Fact]
    public async Task Ef_composition_is_opt_in_owned_and_cookie_events_are_selected()
    {
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new IdentityIamEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }, isDevelopmentOrDemo: true);
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new IdentityIamEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }, isDevelopmentOrDemo: true);

        Assert.Single(services, x => x.ServiceType == typeof(IdentityAuthorityStoreBackend));
        Assert.Single(services, x => x.ServiceType == typeof(IUserStore));
        Assert.Single(services, x => x.ServiceType == typeof(IRoleStore));
        foreach (var serviceType in new[]
        {
            typeof(IUserStore<AspNetCoreIdentityUser>),
            typeof(IUserPasswordStore<AspNetCoreIdentityUser>),
            typeof(IUserSecurityStampStore<AspNetCoreIdentityUser>),
            typeof(IUserEmailStore<AspNetCoreIdentityUser>),
            typeof(IUserLockoutStore<AspNetCoreIdentityUser>),
            typeof(IUserPhoneNumberStore<AspNetCoreIdentityUser>),
            typeof(IUserTwoFactorStore<AspNetCoreIdentityUser>),
            typeof(IUserLoginStore<AspNetCoreIdentityUser>),
            typeof(IUserClaimStore<AspNetCoreIdentityUser>),
            typeof(IUserRoleStore<AspNetCoreIdentityUser>),
            typeof(IUserAuthenticationTokenStore<AspNetCoreIdentityUser>),
            typeof(IUserAuthenticatorKeyStore<AspNetCoreIdentityUser>),
            typeof(IUserTwoFactorRecoveryCodeStore<AspNetCoreIdentityUser>)
        })
            Assert.Single(services, descriptor => descriptor.ServiceType == serviceType && descriptor.ImplementationType == typeof(EfCoreIdentityUserStore));
        foreach (var serviceType in new[] { typeof(IRoleStore<IdentityRole>), typeof(IRoleClaimStore<IdentityRole>) })
            Assert.Single(services, descriptor => descriptor.ServiceType == serviceType && descriptor.ImplementationType == typeof(EfCoreIdentityRoleStore));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<IUserStore>(), scope.ServiceProvider.GetRequiredService<IRevisionAwareUserStore>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<IRoleStore>(), scope.ServiceProvider.GetRequiredService<IRevisionAwareRoleStore>());
        Assert.Equal(typeof(EfCoreIdentityCookieEvents), scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AspNetCoreIdentityDefaults.CookieScheme).EventsType);
        var schemes = await scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Single(schemes, scheme => scheme.Name == AspNetCoreIdentityDefaults.CookieScheme);
    }

    [Fact]
    public void Equivalent_seeded_composition_registers_one_seeder()
    {
        var services = new ServiceCollection();
        var persistence = new IdentityIamEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        };

        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            persistence,
            new IdentitySeedOptions
            {
                UserName = "admin",
                Password = "Correct Horse1!",
                Email = "admin@example.test",
                RoleName = "Administrators"
            });
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            persistence,
            new IdentitySeedOptions
            {
                UserName = "admin",
                Password = "Correct Horse1!",
                Email = "admin@example.test",
                RoleName = "Administrators"
            });

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EfCoreIdentitySeeder));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IOptions<IdentitySeedOptions>));
        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(IShellInitializer)));
    }

    [Fact]
    public void Independently_created_equivalent_identity_callbacks_are_idempotent()
    {
        var services = new ServiceCollection();
        var persistence = new IdentityIamEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        };

        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            persistence,
            configureIdentity: options =>
            {
                options.DisplayName = "Local Identity";
                options.DefaultTenantId = "tenant-a";
                options.AllowedReturnUrlOrigins.Add("https://studio.example.test");
            });
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            persistence,
            configureIdentity: options =>
            {
                options.DisplayName = "Local Identity";
                options.DefaultTenantId = "tenant-a";
                options.AllowedReturnUrlOrigins.Add("https://studio.example.test");
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value;
        Assert.Equal("Local Identity", options.DisplayName);
        Assert.Equal("tenant-a", options.DefaultTenantId);
        Assert.Equal(["https://studio.example.test"], options.AllowedReturnUrlOrigins);
    }

    [Fact]
    public void Different_identity_callback_values_are_rejected_without_mutation()
    {
        var services = new ServiceCollection();
        var persistence = new IdentityIamEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        };
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            persistence,
            configureIdentity: options => options.DisplayName = "First");
        var before = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                persistence,
                configureIdentity: options => options.DisplayName = "Second"));

        Assert.Contains("different framework or seed options", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, services.ToArray());
    }

    [Fact]
    public void Default_authentication_scheme_configurer_preserves_explicit_defaults_and_fills_missing_fallbacks()
    {
        var subject = new ConfigureEfCoreIdentityDefaultAuthenticationSchemes();
        var explicitDefault = new AuthenticationOptions
        {
            DefaultScheme = "explicit",
            DefaultAuthenticateScheme = "authenticate",
            DefaultSignInScheme = "sign-in"
        };
        subject.Configure(explicitDefault);
        Assert.Equal("authenticate", explicitDefault.DefaultAuthenticateScheme);
        Assert.Equal("sign-in", explicitDefault.DefaultSignInScheme);

        var defaults = new AuthenticationOptions();
        subject.Configure(defaults);
        Assert.Equal(AspNetCoreIdentityDefaults.CookieScheme, defaults.DefaultAuthenticateScheme);
        Assert.Equal(AspNetCoreIdentityDefaults.CookieScheme, defaults.DefaultSignInScheme);

        var fallback = new AuthenticationOptions { DefaultAuthenticateScheme = "authenticate" };
        subject.Configure(fallback);
        Assert.Equal("authenticate", fallback.DefaultAuthenticateScheme);
        Assert.Equal(AspNetCoreIdentityDefaults.CookieScheme, fallback.DefaultSignInScheme);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Email_uniqueness_policy_projects_framework_options(bool requireUniqueEmail)
    {
        var options = new IdentityOptions();
        options.User.RequireUniqueEmail = requireUniqueEmail;

        var policy = new EfCoreIdentityEmailUniquenessPolicy(Options.Create(options));

        Assert.Equal(requireUniqueEmail, policy.RequireUniqueEmail);
    }

    [Fact]
    public void Conflicting_framework_registration_is_rejected_before_partial_mutation()
    {
        var services = new ServiceCollection();
        var persistence = new IdentityIamEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        };
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(persistence);
        var descriptorCount = services.Count;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                persistence,
                isDevelopmentOrDemo: true));

        Assert.Contains("different framework or seed options", exception.Message, StringComparison.Ordinal);
        Assert.Equal(descriptorCount, services.Count);
    }

    [Fact]
    public void Unowned_IAM_contract_is_rejected_before_ASP_NET_authority_mutation()
    {
        var services = new ServiceCollection();
        services.AddScoped<IClaimMappingStore>(_ => throw new InvalidOperationException("not resolved"));
        var originalDescriptors = services.ToArray();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                new IdentityIamEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = "Data Source=:memory:"
                }));

        Assert.Contains("unowned host registration", exception.Message, StringComparison.Ordinal);
        Assert.Equal(originalDescriptors, services.ToArray());
    }

    [Fact]
    public async Task Ef_seeder_converges_admin_user_role_and_membership_after_host_schema_initialization()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(initialAdmin: new IdentitySeedOptions
        {
            UserName = "admin",
            Password = "Correct Horse1!",
            Email = "admin@example.test",
            RoleName = "Administrators"
        }, addLogging: true);

        var seeder = scenario.Services.GetRequiredService<EfCoreIdentitySeeder>();
        await seeder.StartAsync(CancellationToken.None);

        var user = await scenario.Users.FindByNameAsync("ADMIN");
        Assert.NotNull(user);
        Assert.Contains("Administrators", await scenario.Users.GetRolesAsync(user!));
        var adminRole = await scenario.Roles.FindByNameAsync("ADMINISTRATORS");
        Assert.NotNull(adminRole);
        var membership = await scenario.Services.GetRequiredService<ITenantMembershipStore>().FindAsync(scenario.TenantId, user!.Id);
        Assert.NotNull(membership);
        Assert.Contains(adminRole!.Id, membership!.RoleIds);
        Assert.IsType<IdentitySeedCoordinator.AlreadyConverged>(await seederResult(scenario, new IdentitySeedOptions
        {
            UserName = "admin",
            Password = "Correct Horse1!",
            Email = "admin@example.test",
            RoleName = "Administrators"
        }));

        static async Task<IdentitySeedCoordinator.SeedResult> seederResult(EfCoreIdentityScenario scenario, IdentitySeedOptions options) =>
            await scenario.Services.GetRequiredService<IdentitySeedCoordinator>().EnsureSeededAsync(options);
    }

    [Fact]
    public async Task Ef_seeder_redacts_password_echoed_by_a_password_policy_validator()
    {
        const string password = "seed-password-never-echo";
        var logs = new List<string>();
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(
            services =>
            {
                services.AddSingleton<IPasswordValidator<AspNetCoreIdentityUser>, EchoingPasswordValidator>();
                services.AddLogging(builder => builder.AddProvider(new CapturingLoggerProvider(logs)));
            },
            initialAdmin: new IdentitySeedOptions
            {
                UserName = "admin",
                Password = password,
                Email = "admin@example.test",
                RoleName = "Administrators"
            });

        var seeder = scenario.Services.GetRequiredService<EfCoreIdentitySeeder>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.StartAsync(CancellationToken.None));

        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logs, message => message.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ef_seeder_does_not_initialize_an_uninitialized_database()
    {
        await using var scenario = await EfCoreIdentityScenario.CreateAsync(
            initialAdmin: new IdentitySeedOptions
            {
                UserName = "admin",
                Password = "Correct Horse1!",
                Email = "admin@example.test",
                RoleName = "Administrators"
            },
            addLogging: true,
            ensureSchema: false);

        var seeder = scenario.Services.GetRequiredService<EfCoreIdentitySeeder>();
        var exception = await Assert.ThrowsAsync<IdentityEntityFrameworkPersistenceException>(() => seeder.StartAsync(CancellationToken.None));
        Assert.Contains("Unable to", exception.Message, StringComparison.Ordinal);

        var connection = scenario.Services.GetRequiredService<IdentityIamDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'identity_users'";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Two_concurrent_ef_seeders_converge_to_one_admin_aggregate_and_membership()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-identity-seeder-race-{Guid.NewGuid():N}.db");
        var seed = new IdentitySeedOptions
        {
            UserName = "admin",
            Password = "Correct Horse1!",
            Email = "admin@example.test",
            RoleName = "Administrators"
        };
        try
        {
            await using var first = await EfCoreIdentityScenario.CreateAsync(
                initialAdmin: seed,
                addLogging: true,
                databasePath: path);
            await using var second = await EfCoreIdentityScenario.CreateAsync(
                initialAdmin: seed,
                addLogging: true,
                databasePath: path);

            await Task.WhenAll(
                first.Services.GetRequiredService<EfCoreIdentitySeeder>().StartAsync(CancellationToken.None),
                second.Services.GetRequiredService<EfCoreIdentitySeeder>().StartAsync(CancellationToken.None));

            var user = await first.Users.FindByNameAsync(seed.UserName);
            Assert.NotNull(user);
            var role = await first.Roles.FindByNameAsync(seed.RoleName);
            Assert.NotNull(role);
            Assert.True(await first.Users.IsInRoleAsync(user!, seed.RoleName));

            var db = first.Services.GetRequiredService<IdentityIamDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(x => x.TenantId == first.TenantId));
            Assert.Equal(1, await db.Roles.CountAsync(x => x.TenantId == first.TenantId));
            Assert.Equal(1, await db.UserRoles.CountAsync(x => x.TenantId == first.TenantId));
            Assert.Equal(1, await db.TenantMemberships.CountAsync(x => x.TenantId == first.TenantId));
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }

    [Fact]
    public void Feature_can_be_configured_without_adding_a_second_authority_marker()
    {
        var services = new ServiceCollection();
        new AspNetCoreIdentityEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        }.ConfigureServices(services);
        Assert.Single(services, x => x.ServiceType == typeof(IdentityAuthorityStoreBackend));
        Assert.Contains(services, x => x.ServiceType == typeof(IUserStore<AspNetCoreIdentityUser>));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IOptions<IdentitySeedOptions>));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        foreach (var serviceType in new[]
        {
            typeof(IdentityIamDbContext),
            typeof(IUserStore),
            typeof(IRevisionAwareUserStore),
            typeof(IRoleStore),
            typeof(IRevisionAwareRoleStore),
            typeof(IPagedRoleStore),
            typeof(IClaimMappingStore),
            typeof(IRevisionAwareClaimMappingStore),
            typeof(IPagedClaimMappingStore),
            typeof(IExternalIdentityStore),
            typeof(IRevisionAwareExternalIdentityStore),
            typeof(IPagedExternalIdentityStore),
            typeof(ITenantMembershipStore),
            typeof(IRevisionAwareTenantMembershipStore),
            typeof(IApplicationStore),
            typeof(IRevisionAwareApplicationStore),
            typeof(ICredentialStore),
            typeof(IRevisionAwareCredentialStore),
            typeof(IUserStore<AspNetCoreIdentityUser>),
            typeof(IUserLoginStore<AspNetCoreIdentityUser>),
            typeof(IUserClaimStore<AspNetCoreIdentityUser>),
            typeof(IUserRoleStore<AspNetCoreIdentityUser>),
            typeof(IRoleStore<IdentityRole>),
            typeof(IRoleClaimStore<IdentityRole>),
            typeof(IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>),
            typeof(IIdentityEmailUniquenessPolicy),
            typeof(IAuthenticationSessionInvalidator),
            typeof(EfCoreIdentityCookieEvents)
        })
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(serviceType));
    }

    [Theory]
    [InlineData("admin", null)]
    [InlineData(null, "Correct Horse1!")]
    public void Feature_rejects_half_configured_seed(string? userName, string? password)
    {
        var services = new ServiceCollection();
        var feature = new AspNetCoreIdentityEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            SeedAdminUserName = userName,
            SeedAdminPassword = password
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            feature.ConfigureServices(services));

        Assert.Contains("FoundationIdentityAspNetCoreIdentityEntityFrameworkCore", exception.Message, StringComparison.Ordinal);
        Assert.Contains("both SeedAdminUserName and SeedAdminPassword", exception.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(null, null, "admin@elsa.local", IdentitySeedOptions.DefaultRoleName, true)]
    [InlineData("owner@example.test", "Owners", "owner@example.test", "Owners", false)]
    public void Feature_applies_seed_defaults_and_explicit_overrides(
        string? configuredEmail,
        string? configuredRole,
        string expectedEmail,
        string expectedRole,
        bool isDevelopment)
    {
        var services = new ServiceCollection();
        new AspNetCoreIdentityEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            IsDevelopmentOrDemo = isDevelopment,
            SeedAdminUserName = "admin",
            SeedAdminPassword = "Correct Horse1!",
            SeedAdminEmail = configuredEmail,
            SeedAdminRoleName = configuredRole
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var seed = provider.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;
        Assert.Equal("admin", seed.UserName);
        Assert.Equal("Correct Horse1!", seed.Password);
        Assert.Equal(expectedEmail, seed.Email);
        Assert.Equal(expectedRole, seed.RoleName);
        Assert.Equal(isDevelopment, seed.IsDevelopmentSeed);
    }
}

internal sealed class DeliberateLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => Normalize("name", name);
    public string? NormalizeEmail(string? email) => Normalize("email", email);

    private static string? Normalize(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{prefix}::{value.Trim().ToLowerInvariant()}";
}

internal sealed class EchoingPasswordValidator : IPasswordValidator<AspNetCoreIdentityUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AspNetCoreIdentityUser> manager, AspNetCoreIdentityUser user, string? password) =>
        Task.FromResult(IdentityResult.Failed(new IdentityError { Code = password ?? "", Description = password ?? "" }));
}

internal sealed class CapturingLoggerProvider(ICollection<string> messages) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);
    public void Dispose() { }

    private sealed class CapturingLogger(ICollection<string> messages) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}

internal sealed class EfCoreIdentityScenario : IAsyncDisposable
{
    private readonly string databasePath;
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;

    private EfCoreIdentityScenario(string databasePath, ServiceProvider provider, AsyncServiceScope scope)
    {
        this.databasePath = databasePath;
        this.provider = provider;
        this.scope = scope;
    }

    public IServiceProvider Services => scope.ServiceProvider;
    public UserManager<AspNetCoreIdentityUser> Users => Services.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
    public RoleManager<IdentityRole> Roles => Services.GetRequiredService<RoleManager<IdentityRole>>();
    public string TenantId { get; } = "tenant-a";

    public static async Task<EfCoreIdentityScenario> CreateAsync(
        Action<IServiceCollection>? configure = null,
        IdentitySeedOptions? initialAdmin = null,
        bool addLogging = false,
        string? databasePath = null,
        bool ensureSchema = true)
    {
        var path = databasePath ?? Path.Combine(Path.GetTempPath(), $"elsa-identity-contract-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        if (addLogging)
            services.AddLogging();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            new IdentityIamEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = $"Data Source={path}" },
            initialAdmin,
            isDevelopmentOrDemo: true);
        services.Configure<AspNetCoreIdentityOptions>(options => options.DefaultTenantId = "tenant-a");
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateAsyncScope();
        try
        {
            var serviceProvider = scope.ServiceProvider;
            serviceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            if (ensureSchema)
                await serviceProvider.GetRequiredService<IdentityIamDbContext>().Database.EnsureCreatedAsync();
            return new EfCoreIdentityScenario(path, provider, scope);
        }
        catch
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            DeleteDatabase(path);
            throw;
        }
    }

    public async Task<AspNetCoreIdentityUser> CreateUserAsync(string userName, string? email = null)
    {
        var user = new AspNetCoreIdentityUser
        {
            Id = $"user-{Guid.NewGuid():N}",
            TenantId = TenantId,
            UserName = userName,
            Email = email ?? $"{userName.ToLowerInvariant()}@example.test"
        };
        var result = await Users.CreateAsync(user, "Correct Horse1!");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
        return user;
    }

    public async Task<IdentityRole> CreateRoleAsync(string name)
    {
        var role = new IdentityRole { Id = $"role-{Guid.NewGuid():N}", Name = name, NormalizedName = name.ToUpperInvariant() };
        var result = await Roles.CreateAsync(role);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));
        return role;
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
        DeleteDatabase(databasePath);
    }

    private static void DeleteDatabase(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }
}
