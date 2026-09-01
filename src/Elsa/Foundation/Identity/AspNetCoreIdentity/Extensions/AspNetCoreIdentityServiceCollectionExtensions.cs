using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.AspNetCore.Authentication;
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
        services.AddNormalizedAuthenticationType("Elsa.Foundation.Identity");
        services.AddPersistenceCore();

        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<AspNetCoreIdentityOptions>();
        services.AddHttpContextAccessor();

        // CSRF protection for the HTML login form: the GET page embeds an antiforgery token which the POST
        // validates for form posts (JSON API callers are unaffected — they never carry the field/cookie).
        services.AddAntiforgery(options => options.FormFieldName = AspNetCoreIdentityDefaults.AntiforgeryFieldName);

        services.TryAddSingleton<IUserStore, InMemoryIdentityStore>();
        services.TryAddSingleton<IRoleStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());
        services.TryAddSingleton<IExternalIdentityStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());
        services.TryAddSingleton<ITenantMembershipStore>(sp => (InMemoryIdentityStore)sp.GetRequiredService<IUserStore>());

        services.TryAddScoped<IUserManager, AspNetCoreIdentityUserManager>();
        services.TryAddScoped<IRoleManager, AspNetCoreIdentityRoleManager>();
        services.TryAddScoped<IPrincipalFactory, AspNetCoreIdentityPrincipalFactory>();
        services.TryAddScoped<IAuthSessionService, DefaultAuthSessionService>();
        services.TryAddScoped<IIdentitySignInService, AspNetCoreIdentitySignInService>();
        services.TryAddScoped<IdentitySeedCoordinator>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProviderModule, AspNetCoreIdentityAuthenticationProviderModule>());

        return services;
    }

    /// <summary>
    /// Adds ASP.NET Core Identity core (user/role managers, sign-in manager, default token providers), the
    /// Elsa claims-principal factory as ASP.NET Core's <see cref="IUserClaimsPrincipalFactory{TUser}"/>, and
    /// the first-party cookie scheme. Store registration is left to the caller via the returned
    /// <see cref="IdentityBuilder"/> (e.g. <c>.AddEntityFrameworkStores&lt;T&gt;()</c>).
    /// </summary>
    /// <param name="isDevelopmentOrDemo">
    /// When <c>false</c> (production), the sign-in cookie is marked <see cref="CookieSecurePolicy.Always"/> so
    /// it is only ever sent over HTTPS. Under development/demo it relaxes to
    /// <see cref="CookieSecurePolicy.SameAsRequest"/> so a plain-HTTP local host can still establish a session.
    /// </param>
    public static IdentityBuilder AddIdentityCoreServices(this IServiceCollection services, bool isDevelopmentOrDemo = false)
    {
        services.AddNormalizedAuthenticationType(AspNetCoreIdentityDefaults.CookieScheme);

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
                // Secure by default: the session cookie must not travel over plain HTTP in production. Only a
                // development/demo host (which may run on http://localhost) relaxes to SameAsRequest.
                options.Cookie.SecurePolicy = isDevelopmentOrDemo
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.SlidingExpiration = true;
            });

        return builder;
    }
}
