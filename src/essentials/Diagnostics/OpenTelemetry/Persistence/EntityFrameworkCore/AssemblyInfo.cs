using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

// The single, discoverable declaration of this module (ADR 0076 D2). EfModuleCatalog.Discover reads this,
// and EfModuleBinding.For derives the registration class's binding from it (#1872). This module names its
// own default connection instead of the shared "Elsa" one (EfConnectionDefaults).
[assembly: EfModule(
    "Diagnostics.OpenTelemetry",
    typeof(EfOpenTelemetryDbContext),
    HistoryModule = EfOpenTelemetryModule.HistoryModuleName,
    Sqlite = typeof(OpenTelemetrySqliteDbContext),
    SqlServer = typeof(OpenTelemetrySqlServerDbContext),
    PostgreSql = typeof(OpenTelemetryPostgreSqlDbContext),
    MySql = typeof(OpenTelemetryMySqlDbContext),
    DisplayName = "OpenTelemetry",
    DefaultConnectionName = EfOpenTelemetryModule.DefaultConnectionName,
    DefaultSqliteConnectionString = EfOpenTelemetryModule.DefaultSqliteConnectionString)]

// Mirrors the [EfModule] name above into elsa-package.json's extensions.efModules (spec 171 slice 11,
// #1881); EfModuleDescriptorTests guards that the two never drift apart.
[assembly: ManifestExtension("efModules", "Diagnostics.OpenTelemetry")]

// The schema family this module owns (spec 180, FR-001), at the version its skew check reads. A host's readability
// report is derived from it alone (spec 183, FR-020); EfSchemaFamilyDeclarationGuardTests fails the build when a
// family the stores check is not declared here, or is declared at another version.
[assembly: EfSchemaFamily(EfOpenTelemetryModule.SchemaFamily, "Diagnostics.OpenTelemetry", EfOpenTelemetryModule.SchemaVersion)]

// Content and integrity columns: see src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014).
// A trace summary's service and workflow memberships restate its payload, but each is deserialized and merged or
// compared with the upcast payload, so each is content too (#2140).
[assembly: EfSchemaContent(EfOpenTelemetryModule.SchemaFamily, typeof(OpenTelemetryResourceEntity), nameof(OpenTelemetryResourceEntity.PayloadJson))]
[assembly: EfSchemaContent(EfOpenTelemetryModule.SchemaFamily, typeof(OpenTelemetryTraceEntity), nameof(OpenTelemetryTraceEntity.PayloadJson))]
[assembly: EfSchemaContent(EfOpenTelemetryModule.SchemaFamily, typeof(OpenTelemetryTraceSummaryEntity),
    nameof(OpenTelemetryTraceSummaryEntity.PayloadJson), nameof(OpenTelemetryTraceSummaryEntity.ServiceMembershipJson),
    nameof(OpenTelemetryTraceSummaryEntity.WorkflowMembershipJson))]
[assembly: EfSchemaContent(EfOpenTelemetryModule.SchemaFamily, typeof(OpenTelemetrySpanEntity), nameof(OpenTelemetrySpanEntity.PayloadJson))]
[assembly: EfSchemaContent(EfOpenTelemetryModule.SchemaFamily, typeof(OpenTelemetryMetricInstrumentEntity), nameof(OpenTelemetryMetricInstrumentEntity.PayloadJson))]
[assembly: EfSchemaContent(EfOpenTelemetryModule.SchemaFamily, typeof(OpenTelemetryMetricPointEntity), nameof(OpenTelemetryMetricPointEntity.PayloadJson))]
[assembly: EfSchemaContent(EfOpenTelemetryModule.SchemaFamily, typeof(OpenTelemetryLogEntity), nameof(OpenTelemetryLogEntity.PayloadJson))]
