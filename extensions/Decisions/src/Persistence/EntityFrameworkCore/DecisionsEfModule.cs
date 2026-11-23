using Elsa.Persistence.EntityFramework;

namespace Elsa.Decisions.Persistence.EntityFrameworkCore;

/// <summary>Stable Decisions migration history and schema-chain identity.</summary>
public static class DecisionsEfModule
{
    internal const string Name = "Decisions";
    internal const string SchemaFamily = "Decisions";
    internal const string SchemaVersion = "1.0.0";
    public const string HistoryModuleName = "ElsaDecisions";
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(DecisionsEfModule).Assembly, SchemaFamily);
}
