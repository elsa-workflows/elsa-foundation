using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

public sealed class AspNetCoreIdentityRegistrationTests : IAsyncDisposable
{
    private readonly AspNetCoreIdentityFixture _fixture = new();

    [Fact]
    public void EntityFrameworkCoreFeature_Registers_Full_SignIn_Stack()
    {
        // Verifies the feature composes cleanly when enabled (without editing shells.json).
        var services = new ServiceCollection();
        services.AddLogging();
        new AspNetCoreIdentityEntityFrameworkCoreFeature { IsDevelopmentOrDemo = true }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<ApplicationIdentityDbContext>());
        Assert.NotNull(sp.GetRequiredService<SignInManager<AspNetCoreIdentityUser>>());
        Assert.NotNull(sp.GetRequiredService<UserManager<AspNetCoreIdentityUser>>());
        Assert.NotNull(sp.GetRequiredService<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>>());
        Assert.NotNull(sp.GetRequiredService<IIdentitySignInService>());
        Assert.IsType<Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores.EfCoreUserStore>(sp.GetRequiredService<IUserStore>());
        Assert.Contains(sp.GetServices<IAuthenticationProviderModule>(), x => x.ProviderId == AspNetCoreIdentityDefaults.ProviderId);
    }

    [Fact]
    public void Dev_Seeder_Runs_Under_Both_Lifecycle_Hooks()
    {
        // The seeder must run in plain hosts (IHostedService) AND when composed inside a CShells shell
        // (IShellInitializer) — the Elsa.Workbench host does not run shell-scoped hosted services. Registering
        // it under only one hook is the exact regression that left the enabled shell unseeded. The dev/demo
        // seed account now comes from config just like any other, so supply the credentials here.
        var services = new ServiceCollection();
        services.AddLogging();
        new AspNetCoreIdentityEntityFrameworkCoreFeature
        {
            IsDevelopmentOrDemo = true,
            SeedAdminUserName = TestAdmin.UserName,
            SeedAdminPassword = TestAdmin.Password
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            x => x is Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding.IdentitySeeder);
        Assert.Contains(provider.GetServices<CShells.Lifecycle.IShellInitializer>(),
            x => x is Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding.IdentitySeeder);
    }

    [Fact]
    public void Non_Dev_Does_Not_Register_The_Seeder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AspNetCoreIdentityEntityFrameworkCoreFeature { IsDevelopmentOrDemo = false, ConnectionString = "Data Source=:memory:" }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.DoesNotContain(provider.GetServices<CShells.Lifecycle.IShellInitializer>(),
            x => x is Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding.IdentitySeeder);
    }

    [Fact]
    public void Configured_Initial_Admin_Registers_Seeder_When_Not_Dev()
    {
        // A durable-store deployment (dev/demo off) provisions its first admin from config; the seeder must
        // then be registered under both lifecycle hooks, exactly as the dev/demo path is.
        var services = new ServiceCollection();
        services.AddLogging();
        new AspNetCoreIdentityEntityFrameworkCoreFeature
        {
            IsDevelopmentOrDemo = false,
            ConnectionString = "Data Source=:memory:",
            SeedAdminUserName = "root",
            SeedAdminPassword = "S3cret-Passw0rd!"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), x => x is IdentitySeeder);
        Assert.Contains(provider.GetServices<CShells.Lifecycle.IShellInitializer>(), x => x is IdentitySeeder);
    }

    [Fact]
    public async Task Configured_Admin_Is_Seeded_And_Idempotent()
    {
        // dev/demo off + an explicit initial admin: the seeder creates exactly that account (and its
        // administrator role), and a second run is a no-op rather than a duplicate/failure.
        // One database name for every DbContext instance, so the seeder's scope and the assertion scope
        // read the same store (the configure callback runs per-instance).
        var databaseName = $"seed-{Guid.NewGuid():n}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
            isDevelopmentOrDemo: false,
            configureDbContext: builder => builder.UseInMemoryDatabase(databaseName),
            initialAdmin: new IdentitySeedOptions
            {
                UserName = "root",
                Password = "S3cret-Passw0rd!",
                Email = "root@corp.example",
                RoleName = "custom-admins"
            });

        await using var provider = services.BuildServiceProvider();
        var seeder = provider.GetRequiredService<IdentitySeeder>();

        await seeder.StartAsync(CancellationToken.None);
        await seeder.StartAsync(CancellationToken.None); // idempotent

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var users = sp.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var seeded = users.Users.Where(x => x.UserName == "root").ToList();
        Assert.Single(seeded);
        Assert.Equal("root@corp.example", seeded[0].Email);

        // The seeded admin is wired to a role carrying the all-access permission the API surface requires.
        var tenantId = AspNetCoreIdentityDefaults.DefaultTenantId;
        var record = await sp.GetRequiredService<IUserStore>().FindAsync(tenantId, seeded[0].Id);
        Assert.NotNull(record);
        var roles = sp.GetRequiredService<IRoleStore>();
        var adminRole = (await roles.ListAsync(tenantId)).Single(r => record!.RoleIds.Contains(r.Id));
        Assert.Equal("custom-admins", adminRole.Name); // the configured role name flowed through
        Assert.Contains(IdentitySeeder.AllAccessPermission, adminRole.Permissions);
    }

    [Fact]
    public async Task LocalProvider_Is_Surfaced_With_LoginPage_Challenge()
    {
        await using var scope = _fixture.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAuthenticationProviderResolver>();

        var descriptor = await resolver.FindAsync(AspNetCoreIdentityDefaults.ProviderId, allowGlobalFallback: true);

        Assert.NotNull(descriptor);
        Assert.Equal("password", descriptor!.Kind);
        Assert.Equal("/" + AspNetCoreIdentityDefaults.LoginRoute, descriptor.Challenge?.Url);
        Assert.Equal(AspNetCoreIdentityDefaults.CookieScheme, descriptor.Challenge?.Scheme);
    }

    [Fact]
    public async Task ClaimsPrincipalFactory_Projects_Tenant_Roles_And_Permissions()
    {
        await using var scope = _fixture.CreateScope();
        var sp = scope.ServiceProvider;

        var roles = sp.GetRequiredService<IRoleStore>();
        await roles.SaveAsync(new RoleRecord("role-x", "tenant-a", "Editor", null, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersManage }, System: false));

        var users = sp.GetRequiredService<IUserStore>();
        await users.SaveAsync(new UserRecord(
            "user-x", "tenant-a", "frank", "frank@example.com", "Frank",
            UserStatus.Active, ResourceOwnership.Foundation,
            new HashSet<string> { "role-x" },
            new HashSet<string> { DefaultIdentityPermissionKeys.IdentityRolesRead }));

        var factory = sp.GetRequiredService<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>>();
        var principal = await factory.CreateAsync(new AspNetCoreIdentityUser { Id = "user-x", TenantId = "tenant-a", UserName = "frank", DisplayName = "Frank" });

        var permissions = principal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value).ToList();
        Assert.Equal("tenant-a", principal.FindFirst(IdentityClaimTypes.TenantId)?.Value);
        Assert.Contains("user-x", principal.FindAll(ClaimTypes.NameIdentifier).Select(x => x.Value));
        Assert.Contains("role-x", principal.FindAll(IdentityClaimTypes.Role).Select(x => x.Value));
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityRolesRead, permissions); // direct
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersManage, permissions); // role-granted
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
