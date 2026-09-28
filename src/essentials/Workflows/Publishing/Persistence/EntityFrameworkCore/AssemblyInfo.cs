using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872).
[assembly: EfModule(
    "Workflows.Publishing",
    typeof(PublishingSnapshotReviewDbContext),
    HistoryModule = PublishingSnapshotReviewEfModule.HistoryModuleName,
    Sqlite = typeof(PublishingSnapshotReviewSqliteDbContext),
    SqlServer = typeof(PublishingSnapshotReviewSqlServerDbContext),
    PostgreSql = typeof(PublishingSnapshotReviewPostgreSqlDbContext),
    MySql = typeof(PublishingSnapshotReviewMySqlDbContext),
    DisplayName = "Publishing")]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Workflows.Publishing")]

// The schema families this module owns (spec 180, FR-001), each at the version its skew check reads. A host's
// readability report is derived from these alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the
// build when a family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(PublishingLedgerEfModule.SchemaFamily, "Workflows.Publishing", PublishingLedgerEfModule.ContentSchemaVersion)]
[assembly: EfSchemaFamily(PublishingPolicyProjectionEfModule.SchemaFamily, "Workflows.Publishing", PublishingPolicyProjectionEfModule.SchemaVersion)]
[assembly: EfSchemaFamily(PublishingSnapshotReviewEfModule.SchemaFamily, "Workflows.Publishing", PublishingSnapshotReviewEfModule.SchemaVersion)]

// Content and integrity columns: see EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014).
[assembly: EfSchemaContent(PublishingLedgerEfModule.SchemaFamily, typeof(ActivityPublicationReceiptEntity), nameof(ActivityPublicationReceiptEntity.Content))]
[assembly: EfSchemaContent(PublishingLedgerEfModule.SchemaFamily, typeof(ActivityDraftTestRunEntity), nameof(ActivityDraftTestRunEntity.Content))]
