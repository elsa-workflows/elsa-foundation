namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Internal table identities for the history projection under construction. This declares no EF
/// module/family and registers no Runtime model: those join the generated migrations before use.
/// </summary>
internal static class RuntimeHistoryProjectionSchema
{
    internal const string HeadTable = "elsa_runtime_history_head";
    internal const string EventTable = "elsa_runtime_history_event";
    internal const string BookmarkTable = "elsa_runtime_history_bookmark";
    internal const string ActivityTable = "elsa_runtime_history_activity";
    internal const string BookmarkCountTable = "elsa_runtime_history_bookmark_count";
}
