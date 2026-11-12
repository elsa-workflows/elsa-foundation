using Elsa.Persistence.EntityFramework;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

/// <summary>Stable history identity shared by the built-in provider contexts and module tooling.</summary>
public static class AgentEfModule
{
    /// <summary>Frozen module name used by the canonical history-table naming convention.</summary>
    public const string HistoryModuleName = "ElsaAgent";
    /// <summary>The built-in contexts' history table, also consumed by migration inventory tooling.</summary>
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);

    internal const string TurnTableName = "elsa_agent_turns";
    internal const string SchemaFamily = "Agent";
    internal const string SchemaVersion = "1.0.0";
    /// <summary>The canonical chain used by metadata readers and host schema-readability tooling.</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(AgentEfModule).Assembly, SchemaFamily);
}
