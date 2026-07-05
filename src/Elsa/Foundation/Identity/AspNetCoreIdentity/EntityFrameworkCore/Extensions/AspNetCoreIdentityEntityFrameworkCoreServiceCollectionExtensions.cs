using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;

public static class AspNetCoreIdentityEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Wires the durable EF Core substrate for the ASP.NET Core Identity provider: registers
    /// <see cref="ApplicationIdentityDbContext"/>, replaces the in-memory Elsa IAM stores with EF-backed
    /// adapters over that context, adds ASP.NET Core Identity core (<c>SignInManager</c> + token providers)
    /// with the EF stores and cookie scheme, and — when <paramref name="isDevelopmentOrDemo"/> — seeds an
    /// admin user + role at startup.
    /// </summary>
    /// <param name="configureDbContext">
    /// Configures the provider. When <c>null</c> a Sqlite database (<c>Data Source=identity.db</c>) is used
    /// for durable persistence, or an EF in-memory database when <paramref name="isDevelopmentOrDemo"/> is
    /// set — honouring "keep the in-memory store for IsDevelopmentOrDemo only".
    /// </param>
    public static IServiceCollection AddFoundationAspNetCoreIdentityEntityFrameworkCore(
        this IServiceCollection services,
        bool isDevelopmentOrDemo = false,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        // Provider-neutral half (managers, principal factory, sign-in service, provider module, options).
        services.AddFoundationAspNetCoreIdentity();

        services.AddDbContext<ApplicationIdentityDbContext>(builder =>
        {
            if (configureDbContext is not null)
                configureDbContext(builder);
            else if (isDevelopmentOrDemo)
                builder.UseInMemoryDatabase("elsa-identity");
            else
                builder.UseSqlite("Data Source=identity.db", sqlite =>
                    sqlite.MigrationsAssembly(typeof(ApplicationIdentityDbContext).Assembly.GetName().Name));
        });

        // ASP.NET Core Identity core over the EF stores + cookie scheme + Elsa claims-principal factory.
        // The cookie's SecurePolicy hardens to Always outside development/demo (see AddIdentityCoreServices).
        services.AddIdentityCoreServices(isDevelopmentOrDemo).AddEntityFrameworkStores<ApplicationIdentityDbContext>();

        // Replace the in-memory Elsa IAM stores (registered by AddFoundationAspNetCoreIdentity) with the
        // EF-backed adapters over the same context, so the principal factory and managers read durable data.
        services.ReplaceStore<IUserStore, EfCoreUserStore>();
        services.ReplaceStore<IRoleStore, EfCoreRoleStore>();
        services.ReplaceStore<IExternalIdentityStore, EfCoreExternalIdentityStore>();
        services.ReplaceStore<ITenantMembershipStore, EfCoreTenantMembershipStore>();

        if (isDevelopmentOrDemo)
        {
            // Safety guard: hard-fail startup if this dangerous flag is set outside the Development environment
            // (the in-memory DB + seeded well-known admin must never boot in production).
            services.AddIdentityDevelopmentOrDemoGuard("FoundationIdentityAspNetCoreIdentityEntityFrameworkCore");

            // Register once and expose under both lifecycle hooks: IHostedService for plain hosts/tests, and
            // the CShells IShellInitializer for the shell-composed Elsa.Server host (which does not run
            // shell-scoped hosted services). The seed is idempotent under either.
            services.AddSingleton<IdentitySeeder>();
            services.AddHostedService(sp => sp.GetRequiredService<IdentitySeeder>());
            services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<IdentitySeeder>());
        }

        return services;
    }

    private static void ReplaceStore<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var existing = services.Where(x => x.ServiceType == typeof(TService)).ToList();
        foreach (var descriptor in existing)
            services.Remove(descriptor);

        services.AddScoped<TService, TImplementation>();
    }
}
