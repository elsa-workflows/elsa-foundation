using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

internal static class RuntimeHistoryEfModule
{
    internal const string SchemaFamily = "RuntimeHistory";
    internal const string SchemaVersion = "1.0.0";
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeHistoryEfModule).Assembly, SchemaFamily);
    internal const string HeadTable = "elsa_runtime_history_head";
    internal const string EventTable = "elsa_runtime_history_event";
    internal const string BookmarkTable = "elsa_runtime_history_bookmark";
    internal const string ActivityTable = "elsa_runtime_history_activity";
    internal const string BookmarkCountTable = "elsa_runtime_history_bookmark_count";
}
