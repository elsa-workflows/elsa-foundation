using Elsa.Weaver.Persistence.EntityFrameworkCore;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule("Weaver", typeof(WeaverDbContext), HistoryModule = WeaverEfModule.HistoryModuleName,
    Sqlite = typeof(WeaverSqliteDbContext), SqlServer = typeof(WeaverSqlServerDbContext),
    PostgreSql = typeof(WeaverPostgreSqlDbContext), MySql = typeof(WeaverMySqlDbContext))]
[assembly: ManifestExtension("efModules", "Weaver")]
[assembly: EfSchemaFamily(WeaverEfModule.SchemaFamily, "Weaver", WeaverEfModule.SchemaVersion)]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(AgentSessionRow), nameof(AgentSessionRow.Payload), nameof(AgentSessionRow.PausedTurnPayload),
    nameof(AgentSessionRow.PendingAuditsPayload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(AgentMessageRow), nameof(AgentMessageRow.Payload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(AgentContextRow), nameof(AgentContextRow.Payload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(AgentProposalRow), nameof(AgentProposalRow.Payload))]
[assembly: EfSchemaContent(WeaverEfModule.SchemaFamily, typeof(AgentAuditRow), nameof(AgentAuditRow.Payload))]
[assembly: EfSchemaIntegrity(WeaverEfModule.SchemaFamily, typeof(AgentDisclosureSnapshotRow), nameof(AgentDisclosureSnapshotRow.Canonical),
    "The canonical text a disclosure digest was taken over; it is kept byte-identical and verified against its digest, never upcast.")]
