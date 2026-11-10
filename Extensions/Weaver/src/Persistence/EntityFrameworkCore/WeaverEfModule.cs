using Elsa.Persistence.EntityFramework;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore;

/// <summary>Stable history identity shared by the built-in provider contexts and module tooling.</summary>
public static class WeaverEfModule
{
    /// <summary>Frozen module name used by the canonical history-table naming convention.</summary>
    public const string HistoryModuleName = "ElsaWeaver";
    /// <summary>The built-in contexts' history table, also consumed by migration inventory tooling.</summary>
    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);

    internal const string Name = "Weaver";
    internal const string SchemaFamily = "Weaver";
    internal const string SchemaVersion = "1.0.0";

    internal const string SessionTableName = "elsa_weaver_sessions";
    internal const string MessageTableName = "elsa_weaver_messages";
    internal const string SnapshotTableName = "elsa_weaver_disclosure_snapshots";
    internal const string ContextTableName = "elsa_weaver_context";
    internal const string ProposalTableName = "elsa_weaver_proposals";
    internal const string CallKeyTableName = "elsa_weaver_call_keys";
    internal const string AuditTableName = "elsa_weaver_audit";
    internal const string AuditOwnerTableName = "elsa_weaver_audit_owners";
    internal const string AuditSequenceTableName = "elsa_weaver_audit_sequence";

    /// <summary>The canonical chain used by metadata readers and host schema-readability tooling.</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(WeaverEfModule).Assembly, SchemaFamily);
}
