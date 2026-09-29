using Elsa.Persistence.EntityFramework;
using Elsa.Samples.Nuplane.Notes;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2): a host, and `dotnet elsa persistence`, read it as
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

#if DEMO_V2
// Release 1.1.0: the family is at 2.0.0 and its chain reaches back to 1.0.0 through one upcaster, so this build reads rows
// of both versions and a host still on release 1.0.0 reads only the older.
[assembly: EfSchemaFamily(NotesModule.Family, NotesModule.Name, NotesModule.CurrentVersion, Upcasters = [typeof(NotesOneToTwo)])]

// The tags are a document: a read upcasts them through the chain, and a write that changes them restamps the row.
[assembly: EfSchemaContent(NotesModule.Family, typeof(NoteRecord), nameof(NoteRecord.TagsJson))]
#else
// Release 1.0.0: one version, no upcasters, and no document columns to upcast.
[assembly: EfSchemaFamily(NotesModule.Family, NotesModule.Name, NotesModule.CurrentVersion)]
#endif
