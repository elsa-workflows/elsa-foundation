using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Schema family for the durable background activity work ledger.</summary>
public static class RuntimeBackgroundActivityWorkEfModule
{
    public const string TableName = "elsa_runtime_background_activity_work";
    public const string SchemaVersion = "1.0.0";
    public const string SchemaFamily = "RuntimeBackgroundActivityWork";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeBackgroundActivityWorkEfModule).Assembly, SchemaFamily);
}
