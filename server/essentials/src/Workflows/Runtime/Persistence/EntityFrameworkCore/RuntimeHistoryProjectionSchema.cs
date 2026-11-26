namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Internal table identities for the journal projections. The separately declared owner chain and
/// Runtime model cover these together with the three derived occurrence shapes.
/// </summary>
internal static class RuntimeHistoryProjectionSchema
{
    internal const string HeadTable = "elsa_runtime_history_head";
    internal const string EventTable = "elsa_runtime_history_event";
    internal const string BookmarkTable = "elsa_runtime_history_bookmark";
    internal const string ActivityTable = "elsa_runtime_history_activity";
    internal const string BookmarkCountTable = "elsa_runtime_history_bookmark_count";
}
