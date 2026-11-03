using Elsa.Connections.Persistence.EntityFrameworkCore;
using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule("Connections", typeof(ConnectionsDbContext), HistoryModule = ConnectionsEfModule.HistoryModuleName,
    Sqlite = typeof(ConnectionsSqliteDbContext), SqlServer = typeof(ConnectionsSqlServerDbContext),
    PostgreSql = typeof(ConnectionsPostgreSqlDbContext), MySql = typeof(ConnectionsMySqlDbContext))]
[assembly: ManifestExtension("efModules", "Connections")]
[assembly: EfSchemaFamily(ConnectionsEfModule.SchemaFamily, "Connections", ConnectionsEfModule.SchemaVersion)]
[assembly: EfSchemaContent(ConnectionsEfModule.SchemaFamily, typeof(ConnectionRecord), nameof(ConnectionRecord.Payload))]
