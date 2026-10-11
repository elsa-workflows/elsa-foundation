using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>
/// Binds the module's context and has its migrator admit it through its finalization gate, exactly as a first-party EF
/// module's feature does: no provider engine is referenced here, the host's loaded engine is bound by name.
/// </summary>
[ShellFeature(name: FeedModule.EntityFrameworkCoreFeature, DisplayName = "Feed module fixture persistence")]
[UsesEfModule(FeedModule.Name)]
public sealed class FeedModuleEntityFrameworkCoreFeature : IShellFeature
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(FeedModuleDbContext));

    public string? ConnectionString { get; set; }

    /// <summary>The engine the module binds: <c>Sqlite</c> or <c>PostgreSql</c>. The engine assembly is the host's.</summary>
    public string Provider { get; set; } = "Sqlite";

    public void ConfigureServices(IServiceCollection services)
    {
        Binding.Select<Action<IServiceCollection>>(
            Provider,
            AddContext<FeedModuleSqliteDbContext>,
            _ => throw new NotSupportedException("The fixture binds Sqlite or PostgreSql."),
            AddContext<FeedModulePostgreSqlDbContext>,
            _ => throw new NotSupportedException("The fixture binds Sqlite or PostgreSql."))(services);
        services.AddEfModuleMigrations<FeedModuleDbContext>(Provider);
    }

    private void AddContext<TContext>(IServiceCollection services)
        where TContext : FeedModuleDbContext
    {
        Binding.AddContext<TContext>(services, pooled: false, (provider, builder) =>
            Binding.Apply(builder, provider, Provider, ConnectionString, connectionName: null));
        services.AddScoped<FeedModuleDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}
