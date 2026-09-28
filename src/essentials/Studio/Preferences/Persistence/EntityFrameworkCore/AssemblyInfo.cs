using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Studio.Preferences",
    typeof(StudioPreferencesDbContext),
    HistoryModule = StudioPreferencesEfModule.HistoryModuleName,
    Sqlite = typeof(StudioPreferencesSqliteDbContext),
    SqlServer = typeof(StudioPreferencesSqlServerDbContext),
    PostgreSql = typeof(StudioPreferencesPostgreSqlDbContext),
    MySql = typeof(StudioPreferencesMySqlDbContext),
    DisplayName = "Studio Preferences")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Studio.Preferences")]

// The schema family this module owns (spec 180, FR-001), at the version its skew check reads. A host's readability
// report is derived from it alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the build when a
// family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(StudioPreferencesEfModule.SchemaFamily, "Studio.Preferences", StudioPreferencesEfModule.SchemaVersion)]

// The family's content columns (spec 180, FR-009 and FR-014): the documents a read upcasts through the family's chain
// before it deserializes them, and a write that changes them restamps. Every document column of the family's tables is
// declared here; EfSchemaContentDeclarationTests fails the build when one is not, or when one declared is not in the
// model, and EfSchemaFamilyDeclarationGuardTests holds every read and write of them to the chain and the stamp.
[assembly: EfSchemaContent(StudioPreferencesEfModule.SchemaFamily, typeof(StudioPreferenceRecord), nameof(StudioPreferenceRecord.ValueJson))]
