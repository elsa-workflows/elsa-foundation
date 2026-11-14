using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule("Weaver", typeof(WeaverDbContext), HistoryModule = WeaverEfModule.HistoryModuleName,
    Sqlite = typeof(WeaverSqliteDbContext), SqlServer = typeof(WeaverSqlServerDbContext),
    PostgreSql = typeof(WeaverPostgreSqlDbContext), MySql = typeof(WeaverMySqlDbContext))]
[assembly: ManifestExtension("efModules", "Weaver")]
[assembly: EfSchemaFamily(WeaverEfModule.SchemaFamily, "Weaver", WeaverEfModule.SchemaVersion, Upcasters = [typeof(WeaverEffectsV1Upcaster)])]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(WeaverSessionRow), nameof(WeaverSessionRow.Payload), nameof(WeaverSessionRow.PausedTurnPayload),
    nameof(WeaverSessionRow.PendingAuditsPayload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(WeaverMessageRow), nameof(WeaverMessageRow.Payload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(WeaverContextRow), nameof(WeaverContextRow.Payload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(WeaverProposalRow), nameof(WeaverProposalRow.Payload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(WeaverAuditRow), nameof(WeaverAuditRow.Payload))]
[assembly: EfSchemaIntegrity(WeaverEfModule.SchemaFamily, typeof(WeaverDisclosureSnapshotRow), nameof(WeaverDisclosureSnapshotRow.Canonical),
    "The canonical text a disclosure digest was taken over; it is kept byte-identical and verified against its digest, never upcast.")]
