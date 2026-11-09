using Elsa.Persistence.EntityFramework;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;

/// <summary>Stable history identity shared by the built-in provider contexts and module tooling.</summary>
public static class SpecialistsEfModule
{
    /// <summary>Frozen module name used by the canonical history-table naming convention.</summary>
    public const string HistoryModuleName = "ElsaAgentSpecialists";
    /// <summary>The built-in contexts' history table, also consumed by migration inventory tooling.</summary>
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);

    internal const string TurnTableName = "elsa_specialist_turns";
    internal const string SchemaFamily = "Specialists";
    internal const string SchemaVersion = "1.0.0";
    /// <summary>The canonical chain used by metadata readers and host schema-readability tooling.</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(SpecialistsEfModule).Assembly, SchemaFamily);
}
