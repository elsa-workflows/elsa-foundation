using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Elsa.Foundation.Identity.OpenIddict.Extensions;

public static class OpenIddictIdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenIddict identity module: the OpenIddict server (JWT access tokens; issuer, lifetimes
    /// and symmetric credentials from options), local bearer validation (registered as the
    /// <c>OpenIddict.Validation.AspNetCore</c> authentication scheme, with token-entry validation so
    /// revocation is immediate), EF Core token stores over the companion
    /// <see cref="OpenIddictIdentityDbContext"/>, the <see cref="ITokenService"/> implementation, and the
    /// <see cref="OpenIddictIdentityDefaults.SelectorScheme"/> policy scheme that routes each request to the
    /// local bearer handler, the external JwtBearer scheme, or the identity cookie scheme.
    /// </summary>
    /// <param name="configureDbContext">
    /// Configures the token-store provider. When <c>null</c>, a Sqlite database
    /// (<see cref="OpenIddictIdentityDefaults.DefaultConnectionString"/> — the shared identity database file,
    /// with the module's own migrations-history table) is used, or an EF in-memory database when
    /// <see cref="OpenIddictIdentityOptions.IsDevelopmentOrDemo"/> is set.
    /// </param>
    public static IServiceCollection AddFoundationIdentityOpenIddict(
        this IServiceCollection services,
        Action<OpenIddictIdentityOptions>? configure = null,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        services.AddFoundationIdentityAbstractions();

        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<OpenIddictIdentityOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthenticationProviderModule, OpenIddictAuthenticationProviderModule>());

        // Scoped: the service issues/validates through the (scoped) OpenIddict token manager.
        services.TryAddScoped<ITokenService, OpenIddictTokenService>();

        // Server credentials/issuer/lifetimes are applied at options-resolution time so keys configured by
        // the host after feature registration are honored.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<OpenIddictServerOptions>, ConfigureOpenIddictServerOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<AuthenticationOptions>, ConfigureOpenIddictDefaultAuthenticationSchemes>());

        // Registration-time snapshot for persistence decisions (repo idiom, mirrors the Oidc/EF features).
        var snapshot = new OpenIddictIdentityOptions();
        configure?.Invoke(snapshot);

        services.AddDbContext<OpenIddictIdentityDbContext>(builder =>
        {
            if (configureDbContext is not null)
                configureDbContext(builder);
            else if (snapshot.IsDevelopmentOrDemo)
                builder.UseInMemoryDatabase("elsa-identity-openiddict");
            else
                builder.UseSqlite(snapshot.ConnectionString ?? OpenIddictIdentityDefaults.DefaultConnectionString, sqlite => sqlite
                    .MigrationsAssembly(typeof(OpenIddictIdentityDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(OpenIddictIdentityDefaults.MigrationsHistoryTable, OpenIddictIdentityDbContext.Schema));
        });

        services.AddOpenIddict()
            .AddCore(core => core.UseEntityFrameworkCore().UseDbContext<OpenIddictIdentityDbContext>())
            // No HTTP endpoints are enabled: issuance is the first-party ITokenService exchange, not an OAuth
            // grant. The custom flow marker satisfies OpenIddict's "at least one flow" configuration check and
            // names the exchange. Server options (issuer, lifetimes, credentials) come from
            // ConfigureOpenIddictServerOptions.
            .AddServer(server => server.AllowCustomFlow(OpenIddictIdentityDefaults.FirstPartyGrantType))
            .AddValidation(validation =>
            {
                // Validate against the in-process server's configuration (issuer + keys)…
                validation.UseLocalServer();
                // …including the token entry, so revoked/redeemed tokens are rejected immediately…
                validation.EnableTokenEntryValidation();
                // …and expose it all as an ASP.NET Core authentication handler for Authorization: Bearer.
                validation.UseAspNetCore();
            });

        // The composite entry point: routes bearer/cookie requests to the right scheme per request shape.
        services.AddAuthentication().AddPolicyScheme(OpenIddictIdentityDefaults.SelectorScheme, "Elsa Identity scheme selector", policy =>
        {
            policy.ForwardDefaultSelector = OpenIddictSchemeSelector.SelectScheme;
        });

        if (snapshot.IsDevelopmentOrDemo)
        {
            // Expose the store initializer under both lifecycle hooks: IHostedService for plain hosts/tests
            // and the CShells IShellInitializer for the shell-composed Elsa.Server host (see the initializer's
            // remarks). Ensure-created/migrate is idempotent under either.
            services.AddSingleton<OpenIddictIdentityStoreInitializer>();
            services.AddHostedService(sp => sp.GetRequiredService<OpenIddictIdentityStoreInitializer>());
            services.AddSingleton<CShells.Lifecycle.IShellInitializer>(sp => sp.GetRequiredService<OpenIddictIdentityStoreInitializer>());
        }

        return services;
    }
}
