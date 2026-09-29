using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes;

/// <summary>
/// Binds the module's context and has its migrator admit it through its finalization gate, exactly as a first-party EF
/// module's feature does. No provider engine is referenced here: the host's loaded engine is bound by name, from the
/// <c>ef-provider</c> capability this package's <c>nuplane.json</c> declares.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: NotesModule.EntityFrameworkCoreFeature,
    DisplayName = "Notes persistence",
    Description = "EF Core persistence for the Notes sample. It applies or validates its own migrations when the shell activates, according to Elsa:Persistence:EntityFramework:Migrate:Policy.")]
[UsesEfModule(NotesModule.Name)]
public sealed class NotesEntityFrameworkCoreFeature : IShellFeature
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(NotesDbContext));

    [ManifestSetting(
        DisplayName = "Provider",
        Description = "Relational provider for the Notes context: Sqlite or PostgreSql. Select the same engine with Nuplane:Capabilities:ef-provider.",
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
            AddContext<NotesSqliteDbContext>,
            Unsupported,
            AddContext<NotesPostgreSqlDbContext>,
            Unsupported);
        addContext(services);
        services.AddScoped<NoteStore>();
        services.AddEfModuleMigrations<NotesDbContext>(Provider);
    }

    private void AddContext<TContext>(IServiceCollection services) where TContext : NotesDbContext
    {
        Binding.AddContext<TContext>(services, pooled: false, (provider, builder) =>
            Binding.Apply(builder, provider, Provider, ConnectionString, connectionName: null));
        services.AddScoped<NotesDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    private void Unsupported(IServiceCollection services) =>
        throw new NotSupportedException($"The Notes sample supports the Sqlite and PostgreSql providers, not '{Provider}'.");
}
