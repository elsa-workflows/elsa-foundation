using Elsa.Persistence.EntityFramework;

namespace Elsa.Agent.Persistence.EntityFrameworkCore;

/// <summary>Stable history identity shared by the built-in provider contexts and module tooling.</summary>
public static class AgentEfModule
{
    /// <summary>Frozen module name used by the canonical history-table naming convention.</summary>
    public const string HistoryModuleName = "ElsaAgent";
    /// <summary>The built-in contexts' history table, also consumed by migration inventory tooling.</summary>
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);

    internal const string Name = "Agent";
    internal const string SchemaFamily = "Agent";
    internal const string SchemaVersion = "1.0.0";

    internal const string SessionTableName = "elsa_agent_sessions";
    internal const string MessageTableName = "elsa_agent_messages";
    internal const string SnapshotTableName = "elsa_agent_disclosure_snapshots";
    internal const string ContextTableName = "elsa_agent_context";
    internal const string ProposalTableName = "elsa_agent_proposals";
    internal const string CallKeyTableName = "elsa_agent_call_keys";
    internal const string AuditTableName = "elsa_agent_audit";
    internal const string AuditOwnerTableName = "elsa_agent_audit_owners";
    internal const string AuditSequenceTableName = "elsa_agent_audit_sequence";

    /// <summary>The canonical chain used by metadata readers and host schema-readability tooling.</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(AgentEfModule).Assembly, SchemaFamily);
}
