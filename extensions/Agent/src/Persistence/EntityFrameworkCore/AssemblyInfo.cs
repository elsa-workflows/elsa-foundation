using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule("Agent", typeof(AgentDbContext), HistoryModule = AgentEfModule.HistoryModuleName,
    Sqlite = typeof(AgentSqliteDbContext), SqlServer = typeof(AgentSqlServerDbContext),
    PostgreSql = typeof(AgentPostgreSqlDbContext), MySql = typeof(AgentMySqlDbContext))]
[assembly: ManifestExtension("efModules", "Agent")]
[assembly: EfSchemaFamily(AgentEfModule.SchemaFamily, "Agent", AgentEfModule.SchemaVersion)]
[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentTurnRecord), nameof(AgentTurnRecord.Payload))]
[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentConversationRecord), nameof(AgentConversationRecord.Payload))]

[assembly: EfSchemaContent(AgentEfModule.SchemaFamily, typeof(AgentOperationRecord), nameof(AgentOperationRecord.Payload))]
