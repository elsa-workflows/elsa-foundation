using Elsa.Persistence.EntityFramework;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

internal static class RulesEfModule
{
    public const string Name = "RulesRuntime";
    public const string HistoryModuleName = "RulesRuntime";
    public const string SchemaFamily = "RulesRuntime";
    public const string SchemaVersion = "1.0.0";
    public const string DefaultConnectionName = EfConnectionDefaults.ConnectionName;
    public const string DefaultSqliteConnectionString = EfConnectionDefaults.SqliteConnectionString;
    public const int HashLength = 64;
    public const int IdentifierMaximumLength = 256;
    public const int SchemaVersionMaximumLength = 32;

    public const string RunTableName = "elsa_rules_runtime_runs";
    public const string ReceiptTableName = "elsa_rules_runtime_receipts";
    public const string FactTableName = "elsa_rules_runtime_facts";

    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RulesEfModule).Assembly, SchemaFamily);
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
