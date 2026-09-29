using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Entities;
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

// Content and integrity columns: see src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014). EF materializes this context's rows directly, so the family declares no upcasters (same section).
// An atomic write's receipt replays its authoritative result, deserialized, and compares its mutated units with the
// replayed request's serialization; an upgrade plan and its apply receipt are deserialized and compared with the
// serialization of the plan being applied. Each is content, so each comparison is between two documents in one format
// (#2140).
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityAvailabilitySettingsRecord), nameof(ActivityAvailabilitySettingsRecord.Rules))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionAuthoringState),
    nameof(ActivityDefinitionAuthoringState.ContentAuthority), nameof(ActivityDefinitionAuthoringState.ForkedFrom))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionDraft), nameof(ActivityDefinitionDraft.State))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionDraftLayout), nameof(ActivityDefinitionDraftLayout.Records))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionVersion),
    nameof(ActivityDefinitionVersion.DescriptorPayloadSource), nameof(ActivityDefinitionVersion.InputsSource),
    nameof(ActivityDefinitionVersion.OutputsSource), nameof(ActivityDefinitionVersion.DesignFacetsSource))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionVersionLayout), nameof(ActivityDefinitionVersionLayout.Records))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionVersionPublication),
    nameof(ActivityDefinitionVersionPublication.Contract), nameof(ActivityDefinitionVersionPublication.Provider),
    nameof(ActivityDefinitionVersionPublication.ResourceMeasurements), nameof(ActivityDefinitionVersionPublication.RuntimeRequirements))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionManagementProjectionRevision),
    nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityJson), nameof(ActivityDefinitionManagementProjectionRevision.Head),
    nameof(ActivityDefinitionManagementProjectionRevision.Recommendation))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDependencyEdge),
    nameof(ActivityDependencyEdge.MemberUsage), nameof(ActivityDependencyEdge.NodeOrigin))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDependencyProjectionState), nameof(ActivityDependencyProjectionState.Items))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDraftValidationState), nameof(ActivityDraftValidationState.Diagnostics))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityForkCandidate),
    nameof(ActivityForkCandidate.ReservedDefinition), nameof(ActivityForkCandidate.ReservedAuthoringState), nameof(ActivityForkCandidate.ReservedDraft),
    nameof(ActivityForkCandidate.ReservedLayout), nameof(ActivityForkCandidate.MigrationDiagnostics))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityForkReceipt),
    nameof(ActivityForkReceipt.DefinitionMaterialJson), nameof(ActivityForkReceipt.AuthoringState), nameof(ActivityForkReceipt.Draft),
    nameof(ActivityForkReceipt.Layout), nameof(ActivityForkReceipt.MigrationDiagnostics))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDesignOperationRecord),
    nameof(ActivityDesignOperationRecord.AuthoritativeResultJson), nameof(ActivityDesignOperationRecord.MutatedUnitsJson))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityUpgradePlanRecord), nameof(ActivityUpgradePlanRecord.PlanJson))]
[assembly: EfSchemaContent(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityUpgradeApplyReceiptRecord), nameof(ActivityUpgradeApplyReceiptRecord.ReceiptJson))]

// The management projection's derived authority tokens are integrity columns (spec 180, FR-008): the context derives
// them from the content authority when it saves the row, and only provider SQL reads them, re-deriving the row's
// validity from the stored bytes (ActivityAuthorityValiditySql). No store code deserializes them (#2140).
[assembly: EfSchemaIntegrity(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionManagementProjectionRevision),
    nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityCanonicalJson),
    "The canonical form of ContentAuthorityJson, derived on save; provider SQL compares it as stored bytes with ContentAuthorityJson and its integrity hash, and nothing deserializes it.")]
[assembly: EfSchemaIntegrity(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionManagementProjectionRevision),
    nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityAuthorityKeyJson),
    "The authority key's JSON token, derived on save; provider SQL checks it against the key column as stored bytes, and nothing deserializes it.")]
[assembly: EfSchemaIntegrity(ActivitiesDesignEfModule.SchemaFamily, typeof(ActivityDefinitionManagementProjectionRevision),
    nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthoritySourceIdJson),
    "The source id's JSON token, derived on save; provider SQL checks it against the source column as stored bytes, and nothing deserializes it.")]
