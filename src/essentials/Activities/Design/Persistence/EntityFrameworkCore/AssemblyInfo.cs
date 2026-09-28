using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Activities.Design",
    typeof(ActivitiesDesignDbContext),
    HistoryModule = ActivitiesDesignEfModule.HistoryModuleName,
    Sqlite = typeof(ActivitiesDesignSqliteDbContext),
    SqlServer = typeof(ActivitiesDesignSqlServerDbContext),
    PostgreSql = typeof(ActivitiesDesignPostgreSqlDbContext),
    MySql = typeof(ActivitiesDesignMySqlDbContext),
    DisplayName = "Activities Design")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Activities.Design")]

// The schema family this module owns (spec 180, FR-001), at the version its skew check reads. A host's readability
// report is derived from it alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the build when a
// family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(ActivitiesDesignEfModule.SchemaFamily, "Activities.Design", ActivitiesDesignEfModule.SchemaVersion)]
