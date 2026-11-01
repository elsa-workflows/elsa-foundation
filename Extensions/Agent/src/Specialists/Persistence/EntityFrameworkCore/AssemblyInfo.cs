using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule("Specialists", typeof(SpecialistsDbContext), HistoryModule = SpecialistsEfModule.HistoryModuleName,
    Sqlite = typeof(SpecialistsSqliteDbContext), SqlServer = typeof(SpecialistsSqlServerDbContext),
    PostgreSql = typeof(SpecialistsPostgreSqlDbContext), MySql = typeof(SpecialistsMySqlDbContext))]
[assembly: ManifestExtension("efModules", "Specialists")]
[assembly: EfSchemaFamily(SpecialistsEfModule.SchemaFamily, "Specialists", SpecialistsEfModule.SchemaVersion)]
[assembly: EfSchemaContent(SpecialistsEfModule.SchemaFamily, typeof(SpecialistTurnRecord), nameof(SpecialistTurnRecord.Payload))]
