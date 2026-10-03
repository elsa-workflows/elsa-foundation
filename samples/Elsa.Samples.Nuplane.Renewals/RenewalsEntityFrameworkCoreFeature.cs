using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Renewals;

/// <summary>
/// Binds the module's context and has its migrator admit it through its finalization gate, exactly as a first-party EF
/// module's feature does. No provider engine is referenced here: the host's loaded engine is bound by name, from the
/// <c>ef-provider</c> capability this package's <c>nuplane.json</c> declares.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: RenewalsModule.EntityFrameworkCoreFeature,
    DisplayName = "Renewals persistence",
    Description = "EF Core persistence for the Renewals sample. It applies or validates its own migrations when the shell activates, according to Elsa:Persistence:EntityFramework:Migrate:Policy.")]
[UsesEfModule(RenewalsModule.Name)]
public sealed class RenewalsEntityFrameworkCoreFeature : IShellFeature
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(RenewalsDbContext));

    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the Renewals context: Sqlite or PostgreSql. Select the same engine with Nuplane:Capabilities:ef-provider.",
        Category = "Persistence")]
    public string Provider { get; set; } = "Sqlite";

    [ManifestSetting(
        DisplayName = "Connection string",
        Description = "The database to connect to. Two hosts that share a database name the same one. Sqlite defaults to Data Source=elsa.db.",
        Category = "Persistence",
        Secret = true)]
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var addContext = Binding.Select<Action<IServiceCollection>>(
            Provider,
            AddContext<RenewalsSqliteDbContext>,
            Unsupported,
            AddContext<RenewalsPostgreSqlDbContext>,
            Unsupported);
        addContext(services);
        services.AddScoped<RenewalStore>();
        services.AddEfModuleMigrations<RenewalsDbContext>(Provider);
    }

    private void AddContext<TContext>(IServiceCollection services) where TContext : RenewalsDbContext
    {
        Binding.AddContext<TContext>(services, pooled: false, (provider, builder) =>
            Binding.Apply(builder, provider, Provider, ConnectionString, connectionName: null));
        services.AddScoped<RenewalsDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    private void Unsupported(IServiceCollection services) =>
        throw new NotSupportedException($"The Renewals sample supports the Sqlite and PostgreSql providers, not '{Provider}'.");
}
