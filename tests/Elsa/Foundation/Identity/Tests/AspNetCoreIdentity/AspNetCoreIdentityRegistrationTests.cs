using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Identity;
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
        // (IShellInitializer) — the Elsa.Server host does not run shell-scoped hosted services. Registering
        // it under only one hook is the exact regression that left the enabled shell unseeded.
        var services = new ServiceCollection();
        services.AddLogging();
        new AspNetCoreIdentityEntityFrameworkCoreFeature { IsDevelopmentOrDemo = true }.ConfigureServices(services);

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
