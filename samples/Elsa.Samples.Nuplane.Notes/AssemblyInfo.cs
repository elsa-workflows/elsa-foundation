using Elsa.Persistence.EntityFramework;
using Elsa.Samples.Nuplane.Notes;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module: a host, and `dotnet elsa persistence`, read it as
// metadata to learn the module's name, its provider-derived contexts and its migrations-history table.
[assembly: EfModule(
    NotesModule.Name,
    typeof(NotesDbContext),
    HistoryModule = NotesModule.HistoryModuleName,
    Sqlite = typeof(NotesSqliteDbContext),
    PostgreSql = typeof(NotesPostgreSqlDbContext),
    DisplayName = "Notes (schema-rollout sample)")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules.
[assembly: ManifestExtension("efModules", NotesModule.Name)]
