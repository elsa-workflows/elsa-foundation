using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa3.Models;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>
/// The designer layout an Elsa 3 export carries: each activity's <c>metadata.designer</c> position and size, keyed by the
/// node id the conversion gives it (its <c>nodeId</c>, else its <c>id</c>). An activity Elsa 3 never placed (no metadata,
/// or position and size all zero) has no record, and a workflow with none keeps Elsa 4's default layout.
/// </summary>
internal static class Elsa3DesignerLayout
{
    public static IReadOnlyCollection<DesignMetadataRecord> Records(Elsa3WorkflowDefinition definition)
    {
        var records = new List<DesignMetadataRecord>();
        if (definition.Root is { } root)
            Collect(root, records);
        return records;
    }

    private static void Collect(Elsa3Activity activity, List<DesignMetadataRecord> records)
    {
        if (activity.Metadata?.Designer is { } designer &&
            (designer.Position.X != 0 || designer.Position.Y != 0 || designer.Size.Width != 0 || designer.Size.Height != 0))
        {
            var nodeId = !string.IsNullOrWhiteSpace(activity.NodeId) ? activity.NodeId : activity.Id ?? string.Empty;
            records.Add(new DesignMetadataRecord(
                nodeId,
                designer.Position.X,
                designer.Position.Y,
                designer.Size.Width > 0 ? designer.Size.Width : null,
                designer.Size.Height > 0 ? designer.Size.Height : null));
        }

        foreach (var child in activity.Activities ?? [])
            Collect(child, records);
    }
}
