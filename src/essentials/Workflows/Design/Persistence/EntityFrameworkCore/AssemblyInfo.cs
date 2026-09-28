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

// The family's content columns (spec 180, FR-009 and FR-014). EF materializes this context's rows directly, so its
// value converters deserialize these documents as it reads a row: the materialization interceptor accepts the
// family's current version alone, and the context stamps every row it writes, which is why the family declares no
// upcasters. Every document column of the family's tables is declared here, content or integrity;
// EfSchemaContentDeclarationTests fails the build when one is not, or when one declared is not in the model.
// An atomic write's receipt holds the result it replays to a retrying caller, deserialized into that result, so it is
// content; its fingerprint is checked over the stored bytes first (FR-008) (#2140).
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionDraft), nameof(WorkflowDefinitionDraft.StateSource))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionVersion), nameof(WorkflowDefinitionVersion.StateSource))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionDraftLayout),
    nameof(WorkflowDefinitionDraftLayout.RecordsJson), nameof(WorkflowDefinitionDraftLayout.ActivityPresentationJson))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(WorkflowDefinitionVersionLayout),
    nameof(WorkflowDefinitionVersionLayout.RecordsJson), nameof(WorkflowDefinitionVersionLayout.ActivityPresentationJson))]
[assembly: EfSchemaContent(WorkflowsDesignEfModule.SchemaFamily, typeof(DesignOperationEntity), nameof(DesignOperationEntity.ResultJson))]
