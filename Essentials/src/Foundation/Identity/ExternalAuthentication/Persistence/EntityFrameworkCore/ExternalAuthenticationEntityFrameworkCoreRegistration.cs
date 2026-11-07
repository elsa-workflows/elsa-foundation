using CShells.Features;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

public sealed class ExternalAuthenticationEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
}

public static class ExternalAuthenticationEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(ExternalAuthenticationDbContext));

    public static IServiceCollection AddExternalAuthenticationEntityFrameworkCore(this IServiceCollection services, ExternalAuthenticationEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var registration = new Registration(provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling);
        var existing = services.Select(descriptor => descriptor.ImplementationInstance).OfType<Registration>().SingleOrDefault();
        if (existing is not null && existing != registration) throw new InvalidOperationException("External authentication persistence is already registered with different options.");
        var stores = services.Where(descriptor => descriptor.ServiceType == typeof(IAuthenticationArtifactStore)).ToArray();
        var sessions = services.Where(descriptor => descriptor.ServiceType == typeof(IAuthenticationSessionStore)).ToArray();
        if (existing is not null)
        {
            if (stores.Length != 1 || stores[0].ImplementationType != typeof(EfAuthenticationArtifactStore) ||
                sessions.Length != 1 || sessions[0].ImplementationType != typeof(EfAuthenticationSessionStore))
                throw new InvalidOperationException("External authentication persistence no longer owns its artifact store.");
            return services;
        }
        if (stores.Length != 0 || sessions.Length != 0) throw new InvalidOperationException("Select one external authentication artifact/session authority.");
        services.AddFoundationExternalAuthentication();
        services.AddSingleton(registration);
        var add = Binding.Select<Action<IServiceCollection, ExternalAuthenticationEntityFrameworkCoreOptions>>(provider,
            AddContext<ExternalAuthenticationSqliteDbContext>, AddContext<ExternalAuthenticationSqlServerDbContext>,
            AddContext<ExternalAuthenticationPostgreSqlDbContext>, AddContext<ExternalAuthenticationMySqlDbContext>);
        add(services, options);
        services.EnsureReplacementContract<IAuthenticationArtifactStore, EfAuthenticationArtifactStore>();
        services.EnsureReplacementContract<IAuthenticationSessionStore, EfAuthenticationSessionStore>();
        return services;
    }

    private static void AddContext<T>(IServiceCollection services, ExternalAuthenticationEntityFrameworkCoreOptions options) where T : ExternalAuthenticationDbContext
    {
        Binding.AddContext<T>(services, options.Pooling, (provider, builder) => Binding.Apply(builder, provider,
            options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<ExternalAuthenticationDbContext>(provider => provider.GetRequiredService<T>());
    }
    private sealed record Registration(string Provider, string? ConnectionString, string? ConnectionName, string? Schema, bool Pooling);
}

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "FoundationIdentityExternalAuthenticationEntityFrameworkCore", DisplayName = "External authentication persistence",
    Description = "Durable one-time authentication and preview artifacts with provider-owned schema lifecycle.")]
[UsesEfModule(ExternalAuthenticationEfModule.Name)]
public class ExternalAuthenticationEntityFrameworkCoreFeature : IShellFeature
{
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
    public virtual void ConfigureServices(IServiceCollection services) => services.AddExternalAuthenticationEntityFrameworkCore(new()
    {
        Provider = Provider, ConnectionString = ConnectionString, ConnectionName = ConnectionName, Schema = Schema, Pooling = Pooling
    }).AddEfModuleMigrations<ExternalAuthenticationDbContext>(Provider);
}
