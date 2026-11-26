using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests;

public sealed class EfCoreIdentityStoreTests
{
    [Fact]
    public async Task User_and_role_manager_share_authority_rows_and_all_framework_state()
    {
        await using var database = new TemporarySqliteDatabase("identity");
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            new IdentityIamEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = database.ConnectionString },
            isDevelopmentOrDemo: true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        serviceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
            .Bind(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        await serviceProvider.GetRequiredService<IdentityIamDbContext>().Database.EnsureCreatedAsync();

        var roles = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var role = new IdentityRole { Id = "role-1", Name = "Operators", NormalizedName = "OPERATORS" };
        Assert.True((await roles.CreateAsync(role)).Succeeded);

        var users = serviceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var user = new AspNetCoreIdentityUser
        {
            Id = "user-1", TenantId = "tenant-a", UserName = "Alice", Email = "alice@example.test",
            PhoneNumber = "+31123456789", LockoutEnabled = true, TwoFactorEnabled = true
        };
        var created = await users.CreateAsync(user, "Correct Horse1!");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(x => x.Description)));
        Assert.NotNull(user.ConcurrencyStamp);

        Assert.True((await users.AddToRoleAsync(user, role.Name!)).Succeeded);
        Assert.True((await users.AddClaimAsync(user, new System.Security.Claims.Claim("department", "operations"))).Succeeded);
        await users.SetAuthenticationTokenAsync(user, "test", "token", "value");

        var reopened = await users.FindByNameAsync("ALICE");
        Assert.NotNull(reopened);
        Assert.True(await users.IsInRoleAsync(reopened!, role.Name!));
        Assert.Contains(await users.GetClaimsAsync(reopened!), claim => claim.Type == "department" && claim.Value == "operations");
        Assert.Equal("value", await users.GetAuthenticationTokenAsync(reopened!, "test", "token"));
        Assert.True(reopened!.TwoFactorEnabled);
        Assert.True(reopened.LockoutEnabled);
        Assert.Equal("+31123456789", reopened.PhoneNumber);
    }

    [Fact]
    public async Task Tenant_scope_and_revision_conflicts_are_enforced_by_the_framework_store()
    {
        await using var database = new TemporarySqliteDatabase("identity");
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            new IdentityIamEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = database.ConnectionString },
            isDevelopmentOrDemo: true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        serviceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
            .Bind(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var context = serviceProvider.GetRequiredService<IdentityIamDbContext>();
        await context.Database.EnsureCreatedAsync();

        var manager = serviceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var first = new AspNetCoreIdentityUser { Id = "same", TenantId = "tenant-a", UserName = "first" };
        Assert.True((await manager.CreateAsync(first, "Correct Horse1!")).Succeeded);
        var stale = await manager.FindByIdAsync(first.Id);
        Assert.NotNull(stale);
        Assert.True((await manager.UpdateAsync(first)).Succeeded);
        stale!.DisplayName = "stale write";
        var result = await manager.UpdateAsync(stale);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));

        var otherTenant = new AspNetCoreIdentityUser { Id = "same", TenantId = "tenant-b", UserName = "first" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync(otherTenant, "Correct Horse1!"));
    }

    /// <summary>
    /// The framework adapter reads the Identity IAM family from outside its owning module, so it applies the family's
    /// check itself: a row a newer module version wrote reports skew as itself before the adapter maps any of it.
    /// </summary>
    [Theory]
    [InlineData("users")]
    [InlineData("user claims")]
    [InlineData("logins")]
    [InlineData("tokens")]
    [InlineData("user roles")]
    [InlineData("roles")]
    [InlineData("role claims")]
    public async Task A_row_with_a_newer_schema_reports_skew_before_the_framework_adapter_maps_it(string table)
    {
        await using var database = new TemporarySqliteDatabase("identity");
        var services = new ServiceCollection();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            new IdentityIamEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = database.ConnectionString },
            isDevelopmentOrDemo: true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        serviceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
            .Bind(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var context = serviceProvider.GetRequiredService<IdentityIamDbContext>();
        await context.Database.EnsureCreatedAsync();
        var roles = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = serviceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var role = new IdentityRole { Id = "role-1", Name = "Operators", NormalizedName = "OPERATORS" };
        Assert.True((await roles.CreateAsync(role)).Succeeded);
        Assert.True((await roles.AddClaimAsync(role, new System.Security.Claims.Claim("permission", "read"))).Succeeded);
        var user = new AspNetCoreIdentityUser { Id = "user-1", TenantId = "tenant-a", UserName = "Alice", Email = "alice@example.test" };
        Assert.True((await users.CreateAsync(user, "Correct Horse1!")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role.Name!)).Succeeded);
        Assert.True((await users.AddClaimAsync(user, new System.Security.Claims.Claim("department", "operations"))).Succeeded);
        Assert.True((await users.AddLoginAsync(user, new UserLoginInfo("oidc", "subject-1", "OIDC"))).Succeeded);
        await users.SetAuthenticationTokenAsync(user, "test", "token", "value");

        var (skew, reads) = table switch
        {
            "users" => (SkewAsync(context.Users), new Func<Task>[] { () => users.FindByIdAsync(user.Id), () => users.FindByNameAsync("ALICE"), () => users.FindByEmailAsync("ALICE@EXAMPLE.TEST") }),
            "user claims" => (SkewAsync(context.UserClaims), [() => users.GetClaimsAsync(user)]),
            "logins" => (SkewAsync(context.ExternalIdentities), [() => users.GetLoginsAsync(user), () => users.FindByLoginAsync("oidc", "subject-1")]),
            "tokens" => (SkewAsync(context.UserTokens), [() => users.GetAuthenticationTokenAsync(user, "test", "token")]),
            "user roles" => (SkewAsync(context.UserRoles), [() => users.GetRolesAsync(user)]),
            "roles" => (SkewAsync(context.Roles), [() => roles.FindByIdAsync(role.Id), () => roles.FindByNameAsync("OPERATORS"), () => users.GetRolesAsync(user), () => users.IsInRoleAsync(user, role.Name!)]),
            _ => (SkewAsync(context.RoleClaims), [() => roles.GetClaimsAsync(role)])
        };
        await skew;

        foreach (var read in reads)
        {
            EfSchemaVersionSkewTestSupport.AssertSchemaVersionSkew(
                await Assert.ThrowsAsync<EfSchemaVersionSkewException>(read), "IdentityIam", IdentityIamEfModule.SchemaVersion);
            context.ChangeTracker.Clear();
        }

        async Task SkewAsync<TEntity>(DbSet<TEntity> set) where TEntity : class
        {
            var rows = await set.ToListAsync();
            Assert.NotEmpty(rows);
            await EfSchemaVersionSkewTestSupport.ArrangeSkewedRowAsync(
                context,
                version => rows.ForEach(row => context.Entry(row).Property("SchemaVersion").CurrentValue = version),
                _ => { });
        }
    }
}
