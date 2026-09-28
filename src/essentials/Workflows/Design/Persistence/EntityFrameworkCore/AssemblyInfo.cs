using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;

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

// The schema family this module owns (spec 180, FR-001), at the version its skew check reads. A host's readability
// report is derived from it alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the build when a
// family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(WorkflowsDesignEfModule.SchemaFamily, "Workflows.Design", WorkflowsDesignEfModule.SchemaVersion)]

// Content and integrity columns: see EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014). EF materializes this context's rows directly, so the family declares no upcasters (same section).
// An atomic write's receipt holds the result it replays to a retrying caller, deserialized into that result, so it is
// content; its fingerprint is checked over the stored bytes first (FR-008) (#2140).
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionDraft), nameof(WorkflowDefinitionDraft.StateSource))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionVersion), nameof(WorkflowDefinitionVersion.StateSource))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionDraftLayout),
    nameof(WorkflowDefinitionDraftLayout.RecordsJson), nameof(WorkflowDefinitionDraftLayout.ActivityPresentationJson))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionVersionLayout),
    nameof(WorkflowDefinitionVersionLayout.RecordsJson), nameof(WorkflowDefinitionVersionLayout.ActivityPresentationJson))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(DesignOperationEntity), nameof(DesignOperationEntity.ResultJson))]
