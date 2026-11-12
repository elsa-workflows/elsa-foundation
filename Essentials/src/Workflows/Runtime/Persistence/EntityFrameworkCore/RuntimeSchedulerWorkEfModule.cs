using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// The existing queue's content family. Version one is native work; version two can carry model inputs.
/// A predecessor runtime checks the stamp before claiming, so it cannot acknowledge model work as a native Noop.
/// </summary>
internal static class RuntimeSchedulerWorkEfModule
{
    public const string SchemaFamily = "RuntimeSchedulerWork";
    public const string SchemaVersion = "3.0.0";
    public const string ModelSchemaVersion = "2.0.0";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeSchedulerWorkEfModule).Assembly, SchemaFamily);
}
