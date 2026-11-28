namespace Elsa.Activities.DispatchWorkflow.Runtime.Constants;

/// <summary>Stable identifiers and fixed limits of the <c>BulkDispatchWorkflows</c> activity (spec 1242).</summary>
public static class BulkDispatchWorkflowsConstants
{
    /// <summary>The runtime activity type (FR-001).</summary>
    public const string ActivityType = "Elsa.BulkDispatchWorkflows";

    /// <summary>The catalog key, the activity's CLR full name (FR-001). The Elsa 3 converter targets this key.</summary>
    public const string CatalogTypeKey = "Elsa.Activities.DispatchWorkflow.Runtime.Activities.BulkDispatchWorkflows";

    public const string StructureKind = "elsa.bulk-dispatch-workflows.structure";
    public const string StructureSchemaVersion = "1.0.0";

    /// <summary>Slot that holds the one authored <c>DispatchWorkflow</c> run once per item (FR-003).</summary>
    public const string DispatchSlotName = "BulkDispatchWorkflows.Dispatch";
    public const string DispatchSlotProperty = "dispatch";
    public const string DispatchSlotDisplayName = "Dispatch";

    /// <summary>Optional slot run for a child that completed (FR-006).</summary>
    public const string ChildCompletedSlotName = "BulkDispatchWorkflows.ChildCompleted";
    public const string ChildCompletedSlotProperty = "childCompleted";
    public const string ChildCompletedSlotDisplayName = "Child completed";

    /// <summary>Optional slot run for a child that faulted, was cancelled or could not be dispatched (FR-006).</summary>
    public const string ChildFaultedSlotName = "BulkDispatchWorkflows.ChildFaulted";
    public const string ChildFaultedSlotProperty = "childFaulted";
    public const string ChildFaultedSlotDisplayName = "Child faulted";

    /// <summary>The variable the <c>Dispatch</c> node and the slots read the current item from.</summary>
    public const string CurrentItemVariableName = "currentItem";

    /// <summary>The variable the <c>Dispatch</c> node and the slots read the zero-based item index from.</summary>
    public const string CurrentIndexVariableName = "currentIndex";

    /// <summary>
    /// The most passes open at once (FR-005). A fixed constant, not an author input; tuning it for GA is #5980.
    /// </summary>
    public const int Window = 16;

    /// <summary>
    /// The most items one activity accepts (FR-007): the release 1 cap of <c>docs/architecture/large-fan-out.md</c>
    /// decision 1, shared with the parallel ForEach and under question in #5875.
    /// </summary>
    public const int ItemCap = 1000;

    /// <summary>The fault code raised when <c>Items</c> holds more than <see cref="ItemCap"/> entries.</summary>
    public const string CapExceededFaultCode = "BulkDispatchCapExceeded";
}
