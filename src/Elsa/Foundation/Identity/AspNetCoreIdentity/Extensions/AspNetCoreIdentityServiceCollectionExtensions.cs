using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;

public static class AspNetCoreIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider-neutral half of the ASP.NET Core Identity substrate: the Elsa IAM stores
    /// (in-memory by default — see remarks), the user/role managers, the Elsa principal factory, the
    /// first-party sign-in service, and the local authentication provider module.
    /// </summary>
    /// <remarks>
    /// The durable EF stores, ASP.NET Core Identity core (<c>SignInManager</c>, token providers), the cookie
    /// scheme, and admin seeding are supplied by the separate
    /// <c>FoundationIdentityAspNetCoreIdentityEntityFrameworkCore</c> feature
    /// (<c>AddFoundationAspNetCoreIdentityEntityFrameworkCore</c>), which replaces the in-memory Elsa IAM
    /// stores with EF-backed ones over a shared <c>ApplicationIdentityDbContext</c>. The in-memory store is
    /// retained here as the default only for <c>IsDevelopmentOrDemo</c> / tests.
    /// </remarks>
    public static IServiceCollection AddFoundationAspNetCoreIdentity(this IServiceCollection services, Action<AspNetCoreIdentityOptions>? configure = null)
    {
        services.AddFoundationIdentityAbstractions();

        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<AspNetCoreIdentityOptions>();
        services.AddHttpContextAccessor();

        services.TryAddSingleton<IUserStore, InMemoryIdentityStore>();
        services.TryAddSingleton<IRoleStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());
        services.TryAddSingleton<IExternalIdentityStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());
        services.TryAddSingleton<ITenantMembershipStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());

        services.TryAddScoped<IUserManager, AspNetCoreIdentityUserManager>();
        services.TryAddScoped<IRoleManager, AspNetCoreIdentityRoleManager>();
        services.TryAddScoped<IPrincipalFactory, AspNetCoreIdentityPrincipalFactory>();
        services.TryAddScoped<IAuthSessionService, DefaultAuthSessionService>();
        services.TryAddScoped<IIdentitySignInService, AspNetCoreIdentitySignInService>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProviderModule, AspNetCoreIdentityAuthenticationProviderModule>());

        return services;
    }

    /// <summary>
    /// Adds ASP.NET Core Identity core (user/role managers, sign-in manager, default token providers), the
    /// Elsa claims-principal factory as ASP.NET Core's <see cref="IUserClaimsPrincipalFactory{TUser}"/>, and
    /// the first-party cookie scheme. Store registration is left to the caller via the returned
    /// <see cref="IdentityBuilder"/> (e.g. <c>.AddEntityFrameworkStores&lt;T&gt;()</c>).
    /// </summary>
    public static IdentityBuilder AddIdentityCoreServices(this IServiceCollection services)
    {
        var builder = services
            .AddIdentityCore<AspNetCoreIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 6;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserClaimsPrincipalFactory<AspNetCoreIdentityUser>, AspNetCoreIdentityUserClaimsPrincipalFactory>();

        services
            .AddAuthentication()
            .AddCookie(AspNetCoreIdentityDefaults.CookieScheme, options =>
            {
                options.LoginPath = "/" + AspNetCoreIdentityDefaults.LoginRoute;
                options.Cookie.Name = AspNetCoreIdentityDefaults.CookieScheme;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
            });

        return builder;
    }
}
