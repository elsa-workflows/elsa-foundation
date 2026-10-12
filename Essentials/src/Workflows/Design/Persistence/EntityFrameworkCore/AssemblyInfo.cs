using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module. EfModuleCatalog.Discover reads this,
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

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (#1881);
// EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Workflows.Design")]

// The schema family this module owns, at the version its skew check reads. A host's readability
// report is derived from it alone; every family the stores check must be declared here, at the
// version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(WorkflowsDesignEfModule.SchemaFamily, "Workflows.Design", WorkflowsDesignEfModule.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
// EF materializes this context's rows directly, so the family declares no upcasters (same section).
// An atomic write's receipt holds the result it replays to a retrying caller, deserialized into that result, so it is
// content; its fingerprint is checked over the stored bytes first (#2140).
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionDraft), nameof(WorkflowDefinitionDraft.StateSource))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionVersion), nameof(WorkflowDefinitionVersion.StateSource))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionDraftLayout),
    nameof(WorkflowDefinitionDraftLayout.RecordsJson), nameof(WorkflowDefinitionDraftLayout.ActivityPresentationJson))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionVersionLayout),
    nameof(WorkflowDefinitionVersionLayout.RecordsJson), nameof(WorkflowDefinitionVersionLayout.ActivityPresentationJson))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(DesignOperationEntity), nameof(DesignOperationEntity.ResultJson))]
