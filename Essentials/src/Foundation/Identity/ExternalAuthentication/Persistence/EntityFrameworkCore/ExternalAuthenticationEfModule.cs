using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

public static class ExternalAuthenticationEfModule
{
    public const string Name = "Identity.ExternalAuthentication";
    public const string HistoryModuleName = "ElsaIdentityExternalAuthentication";
    public const string TableName = "identity_authentication_artifacts";
    public const string SchemaFamily = "IdentityExternalAuthentication";
    public const string SchemaVersion = "1.2.0";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(ExternalAuthenticationEfModule).Assembly, SchemaFamily);
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
