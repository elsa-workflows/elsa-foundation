using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

internal static class ActivityTemplatePresentation
{
    public static IReadOnlyList<WorkflowExecutableActivityPresentationRecord> CopyFrom(
        IEnumerable<ActivityNodePresentationRecord> records, ExecutableActivityTemplate template)
    {
        var identities = template.NodesById.Keys
            .Concat(template.DirectDependencies.Select(dependency => dependency.OccurrenceId))
            .ToHashSet(StringComparer.Ordinal);
        return ActivityNodePresentationRecord.NormalizeCollection(records)
            .Where(record => identities.Contains(record.NodeId))
            .Select(record => new WorkflowExecutableActivityPresentationRecord(record.NodeId, record.DisplayName, record.Description))
            .ToArray();
    }
}
