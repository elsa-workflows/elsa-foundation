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

    public void ConfigureServices(IServiceCollection services)
    {
        Binding.AddContext<FeedModuleSqliteDbContext>(services, pooled: false, (provider, builder) =>
            Binding.Apply(builder, provider, "Sqlite", ConnectionString, connectionName: null));
        services.AddScoped<FeedModuleDbContext>(provider => provider.GetRequiredService<FeedModuleSqliteDbContext>());
        services.AddEfModuleMigrations<FeedModuleDbContext>("Sqlite");
    }
}
