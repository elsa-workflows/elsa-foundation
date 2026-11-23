using CShells.Features;
using Elsa.Decisions.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Decisions.Persistence.EntityFrameworkCore;

/// <summary>Relational Decisions persistence. Hosts provision schema through their normal database lifecycle.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ManifestFeatureCategory("Persistence")]
[UsesEfModule("Decisions")]
[ShellFeature(name: "DecisionsEntityFrameworkCore", DisplayName = "Decisions Entity Framework Core", Description = "Persists decision assets, immutable versions, cases and completed test receipts.")]
public class DecisionsEntityFrameworkCoreFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Provider", Description = "Relational provider supplied by the host.", Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";
    [ManifestSetting(DisplayName = "Connection string", Description = "Optional explicit database connection string.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }
    [ManifestSetting(DisplayName = "Connection name", Description = "Named shared database connection.", Category = "Persistence")]
    public string? ConnectionName { get; set; }

    public virtual void ConfigureServices(IServiceCollection services)
    {
        var provider = Provider; var connection = ConnectionString; var name = ConnectionName;
        void Configure(IServiceProvider sp, DbContextOptionsBuilder builder) =>
            EfModuleBinding.For(typeof(DecisionsDbContext)).Apply(builder, sp, provider, connection, name, null);
        switch (EfRelationalProviderBinding.Normalize(provider))
        {
            case "sqlite": Register<DecisionsSqliteDbContext>(services, Configure); break;
            case "sqlserver": Register<DecisionsSqlServerDbContext>(services, Configure); break;
            case "postgresql": Register<DecisionsPostgreSqlDbContext>(services, Configure); break;
            case "mysql": Register<DecisionsMySqlDbContext>(services, Configure); break;
            default: throw new NotSupportedException("The Decisions relational provider is unavailable.");
        }
        services.AddScoped<DecisionsDbContext>(sp => sp.GetRequiredService<IDbContextFactory<DecisionsDbContext>>().CreateDbContext());
        services.AddDecisionsEntityFrameworkCore<DecisionsDbContext>();
        services.AddEfModuleMigrations<DecisionsDbContext>(provider);
    }
    private static void Register<TContext>(IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure) where TContext : DecisionsDbContext
    {
        services.AddDbContextFactory<TContext>(configure, ServiceLifetime.Scoped);
        services.AddScoped<IDbContextFactory<DecisionsDbContext>, ContextFactory<TContext>>();
    }
    private sealed class ContextFactory<TContext>(IDbContextFactory<TContext> factory) : IDbContextFactory<DecisionsDbContext> where TContext : DecisionsDbContext
    {
        public DecisionsDbContext CreateDbContext() => factory.CreateDbContext();
        public async Task<DecisionsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await factory.CreateDbContextAsync(cancellationToken);
    }

}

/// <summary>Registers the CQS provider against a factory for an application-owned mapped context.</summary>
public static class DecisionsEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddDecisionsEntityFrameworkCore<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        DecisionPersistenceCompositionCheck.Validate(services, requireBoth: false);
        services.TryAddScoped<EfDecisionStore<TContext>>();
        services.TryAddScoped<IDecisionReader>(sp => sp.GetRequiredService<EfDecisionStore<TContext>>());
        services.TryAddScoped<IDecisionWriter>(sp => sp.GetRequiredService<EfDecisionStore<TContext>>());
        DecisionPersistenceCompositionCheck.Register(services);
        return services;
    }
}
