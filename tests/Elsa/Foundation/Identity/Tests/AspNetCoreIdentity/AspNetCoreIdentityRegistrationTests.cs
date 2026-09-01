using System.Security.Claims;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Persistence.Core.DependencyInjection;
using Groundwork.Store;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

public sealed class AspNetCoreIdentityRegistrationTests : IAsyncDisposable
{
    private readonly AspNetCoreIdentityFixture _fixture = new();

    [Fact]
    public void GroundworkFeature_Registers_Full_SignIn_Stack()
    {
        var services = CreateServices();
        new AspNetCoreIdentityGroundworkFeature { IsDevelopmentOrDemo = true }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<SignInManager<AspNetCoreIdentityUser>>());
        Assert.NotNull(sp.GetRequiredService<UserManager<AspNetCoreIdentityUser>>());
        Assert.NotNull(sp.GetRequiredService<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>>());
        Assert.NotNull(sp.GetRequiredService<IIdentitySignInService>());
        Assert.IsType<GroundworkIdentityUserStore>(sp.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>());
        Assert.Contains(sp.GetServices<IAuthenticationProviderModule>(), x => x.ProviderId == AspNetCoreIdentityDefaults.ProviderId);
    }

    [Fact]
    public void Dev_Seeder_Runs_Under_Both_Lifecycle_Hooks()
    {
        var services = CreateServices();
        new AspNetCoreIdentityGroundworkFeature
        {
            IsDevelopmentOrDemo = true,
            SeedAdminUserName = TestAdmin.UserName,
            SeedAdminPassword = TestAdmin.Password
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            x => x is GroundworkIdentitySeeder);
        Assert.Contains(provider.GetServices<IShellInitializer>(),
            x => x is GroundworkIdentitySeeder);
    }

    [Fact]
    public void Non_Dev_Does_Not_Register_The_Seeder()
    {
        var services = CreateServices();
        new AspNetCoreIdentityGroundworkFeature { IsDevelopmentOrDemo = false }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.DoesNotContain(provider.GetServices<IShellInitializer>(), x => x is GroundworkIdentitySeeder);
    }

    [Fact]
    public void Configured_Initial_Admin_Registers_Seeder_When_Not_Dev()
    {
        var services = CreateServices();
        new AspNetCoreIdentityGroundworkFeature
        {
            IsDevelopmentOrDemo = false,
            SeedAdminUserName = "root",
            SeedAdminPassword = "S3cret-Passw0rd!"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), x => x is GroundworkIdentitySeeder);
        Assert.Contains(provider.GetServices<IShellInitializer>(), x => x is GroundworkIdentitySeeder);
    }

    [Fact]
    public async Task Configured_Admin_Is_Seeded_And_Idempotent()
    {
        var services = CreateServices();
        services.AddFoundationAspNetCoreIdentityGroundwork(
            new IdentitySeedOptions
            {
                UserName = "root",
                Password = "S3cret-Passw0rd!",
                Email = "root@corp.example",
                RoleName = "custom-admins"
            },
            isDevelopmentOrDemo: false);

        await using var provider = services.BuildServiceProvider();
        var seeder = provider.GetRequiredService<GroundworkIdentitySeeder>();

        await seeder.StartAsync(CancellationToken.None);
        await seeder.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var seeded = await users.FindByNameAsync("root");
        Assert.NotNull(seeded);
        Assert.Equal("root@corp.example", seeded!.Email);

        var record = await sp.GetRequiredService<IUserStore>().FindAsync(seeded.TenantId, seeded.Id);
        Assert.NotNull(record);
        var roles = sp.GetRequiredService<IRoleStore>();
        var adminRole = (await roles.ListAsync(seeded.TenantId)).Single(r => record!.RoleIds.Contains(r.Id));
        Assert.Equal("custom-admins", adminRole.Name);
        Assert.Contains(IdentitySeedCoordinator.AllAccessPermission, adminRole.Permissions);
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
        await roles.SaveAsync(new RoleRecord("role-x", AspNetCoreIdentityDefaults.DefaultTenantId, "Editor", null, new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersManage }, System: false));

        var users = sp.GetRequiredService<IUserStore>();
        await users.SaveAsync(new UserRecord(
            "user-x", AspNetCoreIdentityDefaults.DefaultTenantId, "frank", "frank@example.com", "Frank",
            UserStatus.Active, ResourceOwnership.Foundation,
            new HashSet<string> { "role-x" },
            new HashSet<string> { DefaultIdentityPermissionKeys.IdentityRolesRead }));

        var factory = sp.GetRequiredService<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>>();
        var principal = await factory.CreateAsync(new AspNetCoreIdentityUser { Id = "user-x", TenantId = AspNetCoreIdentityDefaults.DefaultTenantId, UserName = "frank", DisplayName = "Frank" });

        var permissions = principal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value).ToList();
        Assert.Equal(AspNetCoreIdentityDefaults.DefaultTenantId, principal.FindFirst(IdentityClaimTypes.TenantId)?.Value);
        Assert.Contains("user-x", principal.FindAll(ClaimTypes.NameIdentifier).Select(x => x.Value));
        Assert.Contains("role-x", principal.FindAll(IdentityClaimTypes.Role).Select(x => x.Value));
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityRolesRead, permissions);
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersManage, permissions);
    }

    private static ServiceCollection CreateServices()
    {
        var persistence = new IdentityV2TestPersistence();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(persistence);
        services.AddSingleton<IStorageProviderConnection>(p => p.GetRequiredService<IdentityV2TestPersistence>().Connection);
        services.AddPersistenceCore();
        return services;
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
