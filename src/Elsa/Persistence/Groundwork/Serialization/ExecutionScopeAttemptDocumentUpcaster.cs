using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Adds the execution-scope and attempt-lineage fields introduced by version 2 of the activity
/// execution, activity inspection, and scheduler work-item documents. The fields are nullable so
/// historical executions retain their original, unknown provenance instead of inventing it.
/// </summary>
public sealed class ExecutionScopeAttemptDocumentUpcaster(string documentKind) : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind { get; } = documentKind;
    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        content.TryAdd("executionScopeId", null);
        content.TryAdd("attempt", null);

        switch (DocumentKind)
        {
            case ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind:
                AddStateFields(content["state"] as JsonObject);
                break;
            case ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind:
                AddInspectionFields(content["projection"] as JsonObject);
                AddInspectionFields(content["summary"] as JsonObject);
                break;
            case ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind:
                AddStateFields(content["item"] as JsonObject);
                break;
            default:
                throw new InvalidOperationException($"Document kind '{DocumentKind}' does not carry execution-scope provenance.");
        }

        return content;
    }

    private static void AddStateFields(JsonObject? state)
    {
        if (state is null)
            return;

        state.TryAdd("executionScopeId", null);
        state.TryAdd("attempt", null);
        AddAttemptToProvenance(state["provenance"] as JsonObject);
    }

    private static void AddInspectionFields(JsonObject? projection)
    {
        if (projection is null)
            return;

        projection.TryAdd("executionScopeId", null);
        projection.TryAdd("attempt", null);
        AddAttemptToProvenance(projection["provenance"] as JsonObject);
    }

    private static void AddAttemptToProvenance(JsonObject? provenance) => provenance?.TryAdd("attempt", null);
}
