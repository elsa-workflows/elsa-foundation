using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;

/// <summary>
/// Identity seeding: ensures the identity schema exists, then seeds an administrator role (granted the
/// all-access permission plus every catalog permission) and an admin user so the deployment has a first
/// account to sign in with. Runs when the EF feature is registered with <c>isDevelopmentOrDemo: true</c>
/// (well-known dev admin) or with an explicitly configured initial admin (durable store). The account is
/// taken from <see cref="IdentitySeedOptions"/>.
/// </summary>
/// <remarks>
/// Implemented as both an <see cref="IHostedService"/> (for plain hosts / tests, where hosted services run at
/// host start) and a CShells <see cref="IShellInitializer"/> (so it also runs when composed inside a CShells
/// shell — the <see cref="Elsa.Server"/> host — where shell-scoped hosted services are not executed). The
/// seed is idempotent, so running under whichever hook fires is safe.
/// </remarks>
public sealed class IdentitySeeder(
    IServiceProvider services,
    IOptions<AspNetCoreIdentityOptions> identityOptions,
    IOptions<IdentitySeedOptions> seedOptions,
    IPermissionCatalog permissionCatalog,
    ILogger<IdentitySeeder> logger) : IHostedService, IShellInitializer
{
    private IdentitySeedOptions Seed => seedOptions.Value;

    /// <summary>CShells shell-activation hook (see class remarks).</summary>
    public Task InitializeAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    /// <summary>
    /// The all-access permission ("*") that Elsa endpoints secured with <c>ConfigurePermissions()</c> require.
    /// Mirrors <c>Elsa.Api.FastEndpoints.Constants.PermissionNames.All</c>; kept as a literal here so the
    /// seeder does not take a dependency on the API layer.
    /// </summary>
    public const string AllAccessPermission = "*";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The seed account is supplied entirely from configuration (see the feature's SeedAdmin* settings).
        // A registered seeder with no username/password is a wiring error — fail fast rather than attempt to
        // create an unusable account.
        if (string.IsNullOrWhiteSpace(Seed.UserName) || string.IsNullOrWhiteSpace(Seed.Password))
            throw new InvalidOperationException(
                "IdentitySeeder was registered without a configured admin username and password. " +
                "Supply them via the FoundationIdentityAspNetCoreIdentityEntityFrameworkCore SeedAdmin* settings.");

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ApplicationIdentityDbContext>();

        // Relational providers migrate; the in-memory provider has no migrations, so ensure-created instead.
        if (db.Database.IsRelational())
            await db.Database.MigrateAsync(cancellationToken);
        else
            await db.Database.EnsureCreatedAsync(cancellationToken);

        var tenantId = identityOptions.Value.DefaultTenantId;
        var userManager = sp.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var roleStore = sp.GetRequiredService<IRoleStore>();

        var roleId = await EnsureAdminRoleAsync(roleStore, tenantId, cancellationToken);
        await EnsureAdminUserAsync(userManager, sp.GetRequiredService<IUserStore>(), tenantId, roleId, cancellationToken);

        // The password is only echoed for the well-known development/demo admin; a configured production
        // admin logs the username alone so the secret never lands in application logs.
        if (Seed.IsDevelopmentSeed)
            logger.LogInformation(
                "Seeded ASP.NET Core Identity admin account. Sign in at /{LoginRoute} with username '{Username}' and password '{Password}' (development/demo only).",
                AspNetCoreIdentityDefaults.LoginRoute, Seed.UserName, Seed.Password);
        else
            logger.LogInformation(
                "Ensured ASP.NET Core Identity admin account '{Username}'. Sign in at /{LoginRoute}.",
                Seed.UserName, AspNetCoreIdentityDefaults.LoginRoute);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<string> EnsureAdminRoleAsync(IRoleStore roleStore, string tenantId, CancellationToken cancellationToken)
    {
        var existing = (await roleStore.ListAsync(tenantId, cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Name, Seed.RoleName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            return existing.Id;

        var permissions = permissionCatalog.List().Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Grant the administrator the all-access permission ("*") in addition to every catalogued permission.
        // Elsa endpoints secured with ConfigurePermissions() require this wildcard (Api.FastEndpoints
        // PermissionNames.All), and it is not a catalog entry — so without it the seeded admin, though holding
        // every named identity permission, could not reach the workflow/design/runtime API surface.
        permissions.Add(AllAccessPermission);

        var role = new RoleRecord(Guid.NewGuid().ToString("n"), tenantId, Seed.RoleName, "Administrator", permissions, System: true);
        await roleStore.SaveAsync(role, cancellationToken);
        return role.Id;
    }

    private async Task EnsureAdminUserAsync(
        UserManager<AspNetCoreIdentityUser> userManager,
        IUserStore userStore,
        string tenantId,
        string roleId,
        CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByNameAsync(Seed.UserName);
        if (existing is null)
        {
            existing = new AspNetCoreIdentityUser
            {
                Id = Guid.NewGuid().ToString("n"),
                UserName = Seed.UserName,
                Email = Seed.Email,
                TenantId = tenantId,
                DisplayName = "Administrator"
            };

            var result = await userManager.CreateAsync(existing, Seed.Password);
            if (!result.Succeeded)
            {
                // Fail fast: a deployment that boots with an administrator role but no administrator account
                // cannot be signed into (e.g. a configured password that violates the Identity password policy).
                // Surface it at startup rather than silently continuing.
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to seed the '{Seed.UserName}' administrator account: {errors}");
            }
        }

        // Ensure the user record exists in the active IUserStore with the admin role attached. The
        // UserManager writes to the ASP.NET Identity EF tables, but the IUserStore may be backed by a
        // different persistence layer (e.g. Groundwork) that doesn't share those tables.
        var record = await userStore.FindAsync(tenantId, existing.Id, cancellationToken);
        if (record is null)
        {
            record = new UserRecord(
                existing.Id,
                tenantId,
                existing.UserName ?? existing.Id,
                existing.Email,
                existing.DisplayName,
                UserStatus.Active,
                ResourceOwnership.Foundation,
                new HashSet<string> { roleId },
                new HashSet<string>());
            await userStore.SaveAsync(record, cancellationToken);
        }
        else if (!record.RoleIds.Contains(roleId))
        {
            await userStore.SaveAsync(record with { RoleIds = new HashSet<string>(record.RoleIds) { roleId } }, cancellationToken);
        }
    }
}
