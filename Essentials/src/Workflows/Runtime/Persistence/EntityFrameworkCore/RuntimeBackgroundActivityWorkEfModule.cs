using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema family for the durable background activity work ledger.</summary>
public static class RuntimeBackgroundActivityWorkEfModule
{
    public const string TableName = "elsa_runtime_background_activity_work";
    /// <summary>2.0.0: a completed receipt carries an outcome selection instead of one outcome (spec 1208). Receipts are
    /// in-flight work; no backward reader is claimed for 1.0.0 rows (reset-and-republish before 4.0, Q11).</summary>
    public const string SchemaVersion = "2.0.0";
    public const string SchemaFamily = "RuntimeBackgroundActivityWork";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeBackgroundActivityWorkEfModule).Assembly, SchemaFamily);
}
