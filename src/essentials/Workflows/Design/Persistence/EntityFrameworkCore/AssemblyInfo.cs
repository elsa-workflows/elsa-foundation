using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Workflows.Design",
    typeof(WorkflowsDesignDbContext),
    HistoryModule = WorkflowsDesignEfModule.HistoryModuleName,
    Sqlite = typeof(WorkflowsDesignSqliteDbContext),
    SqlServer = typeof(WorkflowsDesignSqlServerDbContext),
    PostgreSql = typeof(WorkflowsDesignPostgreSqlDbContext),
    MySql = typeof(WorkflowsDesignMySqlDbContext),
    DisplayName = "Workflows Design")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Workflows.Design")]
