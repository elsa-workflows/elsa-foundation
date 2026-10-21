using Elsa.Persistence.EntityFramework;

namespace Elsa.Etl.Persistence.EntityFrameworkCore;

internal static class EtlDesignEfModule
{
    public const string Name = "EtlDesign";
    public const string HistoryModuleName = "EtlDesign";
    public const string DefaultConnectionName = "Elsa";
    public const string DefaultSqliteConnectionString = "Data Source=elsa.db";
    public const string SchemaFamily = "EtlDesign";
    public const string SchemaVersion = "1.0.0";
    public const int IdentityMaximumLength = 450;
    public const int EncodedIdentityMaximumLength = ((IdentityMaximumLength * sizeof(char) + 2) / 3) * 4;
    public const int HashLength = 64;
    public const int SchemaVersionMaximumLength = 32;
    public const string DraftTableName = "elsa_etl_definition_drafts";
    public const string PublishedArtifactTableName = "elsa_etl_published_artifacts";

    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(EtlDesignEfModule).Assembly, SchemaFamily);
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
