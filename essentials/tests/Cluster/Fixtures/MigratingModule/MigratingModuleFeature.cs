using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Cluster.Fixtures.MigratingModule;

/// <summary>
/// Binds the module's context and has its migrator apply or validate its migrations at activation, exactly as a first-party
/// EF module's feature does, and answers with the version of the package it was loaded from and the generation of the shell
/// that runs it.
/// </summary>
[ShellFeature(name: MigratingModule.Feature, DisplayName = "Migrating module fixture")]
[UsesEfModule(MigratingModule.Name)]
public sealed class MigratingModuleFeature : IWebShellFeature
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(MigratingModuleDbContext));

    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        Binding.AddContext<MigratingModuleSqliteDbContext>(services, pooled: false, (provider, builder) =>
            Binding.Apply(builder, provider, "Sqlite", ConnectionString, connectionName: null));
        services.AddScoped<MigratingModuleDbContext>(provider => provider.GetRequiredService<MigratingModuleSqliteDbContext>());
        services.AddEfModuleMigrations<MigratingModuleDbContext>("Sqlite");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        endpoints.MapGet(MigratingModule.VersionPath, (IShell shell) =>
            $"{typeof(MigratingModuleFeature).Assembly.GetName().Version!.ToString(3)} generation {shell.Descriptor.Generation}");
}
