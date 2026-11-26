using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;

[assembly: EfModule(ExternalAuthenticationEfModule.Name, typeof(ExternalAuthenticationDbContext),
    HistoryModule = ExternalAuthenticationEfModule.HistoryModuleName,
    Sqlite = typeof(ExternalAuthenticationSqliteDbContext), SqlServer = typeof(ExternalAuthenticationSqlServerDbContext),
    PostgreSql = typeof(ExternalAuthenticationPostgreSqlDbContext), MySql = typeof(ExternalAuthenticationMySqlDbContext),
    DisplayName = "External authentication state")]
[assembly: ManifestExtension("efModules", ExternalAuthenticationEfModule.Name)]
[assembly: EfSchemaFamily(ExternalAuthenticationEfModule.SchemaFamily, ExternalAuthenticationEfModule.Name, ExternalAuthenticationEfModule.SchemaVersion,
    Upcasters = [typeof(AuthenticationStateV1Upcaster), typeof(AuthenticationStateV2Upcaster)])]
[assembly: EfSchemaContent(ExternalAuthenticationEfModule.SchemaFamily, typeof(AuthenticationArtifactEntity), nameof(AuthenticationArtifactEntity.ProtectedPayload))]
[assembly: EfSchemaContent(ExternalAuthenticationEfModule.SchemaFamily, typeof(AuthenticationSessionEntity), nameof(AuthenticationSessionEntity.ProtectedPayload))]
