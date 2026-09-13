using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
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
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-identity-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                new IdentityIamEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = $"Data Source={databasePath}" },
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
        finally
        {
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }.Where(File.Exists))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task Tenant_scope_and_revision_conflicts_are_enforced_by_the_framework_store()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-identity-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                new IdentityIamEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = $"Data Source={databasePath}" },
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
        finally
        {
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" }.Where(File.Exists))
                File.Delete(path);
        }
    }
}
