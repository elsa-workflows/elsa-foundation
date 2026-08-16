using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;
using Elsa.Foundation.Identity.Tests.OpenIddict;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using FastEndpoints;
using Groundwork.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

internal sealed class AspNetCoreIdentityGroundworkHttpFixture : IAsyncDisposable
{
    public const string Password = "Correct1!";
    public const string PrimaryTenant = "tenant-alpha";
    public const string SecondaryTenant = "tenant-beta";
    public const string PrimaryUserId = "user-ada";
    public const string SecondaryUserId = "user-ada-beta";
    public const string UserName = "http-ada";
    public const string Email = "http-ada@example.test";
    public const string DirectPermission = "identity.users.read";
    public const string RolePermission = DefaultIdentityPermissionKeys.IdentityProvidersRead;
    public const string RoleName = "http-administrators";
    public const string RoleId = "http-role";
    public const string SecondaryUserName = "http-beta";
    public const string SecondaryEmail = "http-beta@example.test";
    public const string SecondaryPassword = "TenantBeta1!";
    public const string ClaimsRoute = "/acceptance/claims";
    public const string PermissionRoute = "/acceptance/permission";
    public const string BearerRoute = "/acceptance/bearer";

    private readonly IHost _host;
    private readonly IdentityV2TestPersistence _persistence;

    private AspNetCoreIdentityGroundworkHttpFixture(
        IHost host,
        IdentityV2TestPersistence persistence,
        IReadOnlyCollection<ServiceDescriptor> serviceDescriptors)
    {
        _host = host;
        _persistence = persistence;
        ServiceDescriptors = serviceDescriptors;
    }

    public IServiceProvider Services => _host.Services;

    public IReadOnlyCollection<ServiceDescriptor> ServiceDescriptors { get; }

    public HttpClient CreateClient() => _host.GetTestClient();

    /// <remarks>
    /// Mapping FastEndpoints mutates process-global state, so the calling test class must join
    /// <see cref="Elsa.Foundation.Identity.Tests.FastEndpointsHostCollection"/>.
    /// </remarks>
    public static async Task<AspNetCoreIdentityGroundworkHttpFixture> StartAsync(
        Action<IServiceCollection>? configureServices = null)
    {
        var databaseSuffix = Guid.NewGuid().ToString("n");
        var persistence = new IdentityV2TestPersistence();
        var serviceDescriptors = Array.Empty<ServiceDescriptor>();
        var host = new HostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddSingleton<IStorageProviderConnection>(persistence.Connection);
                    services.AddPersistenceCore(AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
                    services.AddFoundationIdentityOpenIddict(
                        options => options.IsDevelopmentOrDemo = true,
                        configureDbContext: builder => OpenIddictIdentityFixture.ConfigureInMemoryStore(
                            builder,
                            $"openiddict-{databaseSuffix}"));
                    services.AddFoundationAspNetCoreIdentityGroundwork();
                    services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true);
                    services.Configure<AspNetCoreIdentityOptions>(options =>
                        options.DefaultTenantId = AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
                    services.AddFoundationIdentityApi();
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies =
                        [
                            typeof(FoundationIdentityApiOptions).Assembly,
                            typeof(AspNetCoreIdentityDefaults).Assembly
                        ];
                        options.DisableAutoDiscovery = true;
                    });
                    configureServices?.Invoke(services);
                    serviceDescriptors = services.ToArray();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints(config =>
                        {
                            config.Security.PermissionsClaimType = IdentityClaimTypes.Permission;
                            config.Security.RoleClaimType = IdentityClaimTypes.Role;
                        });
                        endpoints.MapGet(ClaimsRoute, async context =>
                        {
                            await context.Response.WriteAsJsonAsync(new
                            {
                                Subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                                TenantId = context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value,
                                Roles = context.User.FindAll(IdentityClaimTypes.Role).Select(claim => claim.Value).Order().ToArray(),
                                Permissions = context.User.FindAll(IdentityClaimTypes.Permission).Select(claim => claim.Value).Order().ToArray()
                            });
                        }).RequireAuthorization();
                        endpoints.MapGet(PermissionRoute, () => Results.Ok("permission-granted"))
                            .RequireAuthorization(policy => policy.RequireClaim(IdentityClaimTypes.Permission, DirectPermission));
                        endpoints.MapGet(BearerRoute, async context =>
                            await context.Response.WriteAsync(context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? string.Empty))
                            .RequireAuthorization();
                    });
                });
            })
            .Build();

        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreatedAsync();

        await host.StartAsync();
        var fixture = new AspNetCoreIdentityGroundworkHttpFixture(host, persistence, serviceDescriptors);
        await fixture.SeedAsync();
        return fixture;
    }

    public async Task InjectAmbiguousEmailUserAsync()
    {
        const string userId = "ambiguous-email-user";
        const string userName = "ambiguous-email-ada";
        var tenantId = AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant;
        var normalizedUserName = userName.ToUpperInvariant();
        var normalizedEmail = Email.ToUpperInvariant();
        var user = new UserRecord(
            userId,
            tenantId,
            userName,
            Email,
            "Ambiguous Email Ada",
            UserStatus.Active,
            ResourceOwnership.Foundation,
            new HashSet<string>(),
            new HashSet<string>());
        await using var scope = Services.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>();
        if (access.Current.Scope != new PersistenceScope(tenantId))
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
        var rows = scope.ServiceProvider.GetRequiredService<GroundworkIdentityRowStore>();
        await new GroundworkUserStore(rows, access).SaveAsync(user);
    }

    public async Task SeedSecondaryTenantUserAsync() =>
        await SeedUserAsync(
            AspNetCoreIdentityGroundworkHttpFixture.SecondaryUserId,
            AspNetCoreIdentityGroundworkHttpFixture.SecondaryTenant,
            SecondaryUserName,
            SecondaryEmail,
            SecondaryPassword,
            "http-secondary-role",
            "http-secondary-administrators");

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        _persistence.Dispose();
    }

    private async Task SeedAsync() =>
        await SeedUserAsync(
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryUserId,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant,
            UserName,
            Email,
            Password,
            RoleId,
            RoleName);

    private async Task SeedUserAsync(
        string userId,
        string tenantId,
        string userName,
        string email,
        string password,
        string roleId,
        string roleName)
    {
        await using var scope = Services.CreateAsyncScope();
        var accessContext = scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>();
        if (accessContext.Current.Scope != new PersistenceScope(tenantId))
        {
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>()
                .Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = new AspNetCoreIdentityUser
        {
            Id = userId,
            TenantId = tenantId,
            UserName = userName,
            Email = email,
            DisplayName = "HTTP Ada",
            SecurityStamp = $"http-security-{userId}",
            ConcurrencyStamp = $"http-user-revision-{userId}",
            LockoutEnabled = true
        };
        var role = new IdentityRole(roleName)
        {
            Id = roleId,
            ConcurrencyStamp = $"http-role-revision-{roleId}"
        };

        RequireSucceeded(await roles.CreateAsync(role));
        RequireSucceeded(await users.CreateAsync(user, password));
        RequireSucceeded(await users.AddToRoleAsync(user, roleName));

        var userStore = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var roleStore = scope.ServiceProvider.GetRequiredService<IRoleStore>();
        var membershipStore = scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>();
        await userStore.SaveAsync(new UserRecord(
            user.Id,
            user.TenantId,
            userName,
            email,
            user.DisplayName,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { roleId },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DirectPermission }));
        await roleStore.SaveAsync(new RoleRecord(
            roleId,
            user.TenantId,
            roleName,
            "HTTP acceptance role",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RolePermission },
            System: false));
        await membershipStore.SaveAsync(new TenantMembershipRecord(
            user.TenantId,
            user.Id,
            TenantMembershipStatus.Active,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { roleId },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    private static void RequireSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));
}
