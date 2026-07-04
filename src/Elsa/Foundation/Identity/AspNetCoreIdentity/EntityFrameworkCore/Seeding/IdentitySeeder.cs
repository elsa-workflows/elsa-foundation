using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;

/// <summary>
/// Development/demo seeding: ensures the identity schema exists, then seeds an administrator role (granted
/// every catalog permission) and an admin user so a fresh checkout can log in. The seeded credentials are
/// logged clearly to the console at startup. Runs only when the EF feature is registered with
/// <c>isDevelopmentOrDemo: true</c>.
/// </summary>
public sealed class IdentitySeeder(
    IServiceProvider services,
    IOptions<AspNetCoreIdentityOptions> identityOptions,
    IPermissionCatalog permissionCatalog,
    ILogger<IdentitySeeder> logger) : IHostedService
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "Password123!";
    public const string AdminEmail = "admin@elsa.local";
    public const string AdminRoleName = "administrator";

    /// <summary>
    /// The all-access permission ("*") that Elsa endpoints secured with <c>ConfigurePermissions()</c> require.
    /// Mirrors <c>Elsa.Api.FastEndpoints.Constants.PermissionNames.All</c>; kept as a literal here so the
    /// seeder does not take a dependency on the API layer.
    /// </summary>
    public const string AllAccessPermission = "*";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

        logger.LogInformation(
            "Seeded ASP.NET Core Identity admin account. Sign in at /{LoginRoute} with username '{Username}' and password '{Password}' (development/demo only).",
            AspNetCoreIdentityDefaults.LoginRoute, AdminUserName, AdminPassword);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<string> EnsureAdminRoleAsync(IRoleStore roleStore, string tenantId, CancellationToken cancellationToken)
    {
        var existing = (await roleStore.ListAsync(tenantId, cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Name, AdminRoleName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            return existing.Id;

        var permissions = permissionCatalog.List().Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Grant the administrator the all-access permission ("*") in addition to every catalogued permission.
        // Elsa endpoints secured with ConfigurePermissions() require this wildcard (Api.FastEndpoints
        // PermissionNames.All), and it is not a catalog entry — so without it the seeded admin, though holding
        // every named identity permission, could not reach the workflow/design/runtime API surface.
        permissions.Add(AllAccessPermission);

        var role = new RoleRecord(Guid.NewGuid().ToString("n"), tenantId, AdminRoleName, "Administrator", permissions, System: true);
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
        var existing = await userManager.FindByNameAsync(AdminUserName);
        if (existing is not null)
            return;

        var user = new AspNetCoreIdentityUser
        {
            Id = Guid.NewGuid().ToString("n"),
            UserName = AdminUserName,
            Email = AdminEmail,
            TenantId = tenantId,
            DisplayName = "Administrator"
        };

        var result = await userManager.CreateAsync(user, AdminPassword);
        if (!result.Succeeded)
        {
            logger.LogError("Failed to seed admin user: {Errors}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        // Attach the admin role via the Elsa store adapter (writes the user-role join row).
        var record = await userStore.FindAsync(tenantId, user.Id, cancellationToken);
        if (record is not null)
            await userStore.SaveAsync(record with { RoleIds = new HashSet<string>(record.RoleIds) { roleId } }, cancellationToken);
    }
}
