using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule("Agent", typeof(AgentDbContext), HistoryModule = AgentEfModule.HistoryModuleName,
    Sqlite = typeof(AgentSqliteDbContext), SqlServer = typeof(AgentSqlServerDbContext),
    PostgreSql = typeof(AgentPostgreSqlDbContext), MySql = typeof(AgentMySqlDbContext))]
[assembly: ManifestExtension("efModules", "Agent")]
[assembly: EfSchemaFamily(AgentEfModule.SchemaFamily, "Agent", AgentEfModule.SchemaVersion)]
[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentSessionRow), nameof(AgentSessionRow.Payload), nameof(AgentSessionRow.PausedTurnPayload),
    nameof(AgentSessionRow.PendingAuditsPayload))]
[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentMessageRow), nameof(AgentMessageRow.Payload))]
[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentContextRow), nameof(AgentContextRow.Payload))]
[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentProposalRow), nameof(AgentProposalRow.Payload))]
[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentAuditRow), nameof(AgentAuditRow.Payload))]
[assembly: EfSchemaIntegrity(AgentEfModule.SchemaFamily, typeof(AgentDisclosureSnapshotRow), nameof(AgentDisclosureSnapshotRow.Canonical),
    "The canonical text a disclosure digest was taken over; it is kept byte-identical and verified against its digest, never upcast.")]
