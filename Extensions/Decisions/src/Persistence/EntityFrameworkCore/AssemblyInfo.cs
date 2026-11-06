using Elsa.Decisions.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule(DecisionsEfModule.Name, typeof(DecisionsDbContext),
    HistoryModule = DecisionsEfModule.HistoryModuleName,
    Sqlite = typeof(DecisionsSqliteDbContext), SqlServer = typeof(DecisionsSqlServerDbContext),
    PostgreSql = typeof(DecisionsPostgreSqlDbContext), MySql = typeof(DecisionsMySqlDbContext),
    DisplayName = "Decisions", DefaultConnectionName = EfConnectionDefaults.ConnectionName,
    DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString)]
[assembly: ManifestExtension("efModules", "Decisions")]
[assembly: EfSchemaFamily(DecisionsEfModule.SchemaFamily, DecisionsEfModule.Name, DecisionsEfModule.SchemaVersion)]
[assembly: EfSchemaContent(DecisionsEfModule.SchemaFamily, typeof(DecisionRecord), nameof(DecisionRecord.Json))]
