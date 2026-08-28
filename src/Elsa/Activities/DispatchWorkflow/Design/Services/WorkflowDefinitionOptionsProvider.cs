using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Design.Services;

/// <summary>Lists tenant-visible definitions that resolve to exactly one live Published source reference.</summary>
public sealed class WorkflowDefinitionOptionsProvider(
    IWorkflowDefinitionStore definitionStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider) : IActivityInputOptionsProvider
{
    public string Key => DispatchWorkflowConstants.WorkflowDefinitionOptionsKey;

    public async ValueTask<IReadOnlyList<ActivityInputOption>> GetOptionsAsync(
        ActivityInputOptionsContext context,
        CancellationToken cancellationToken = default)
    {
        var definitions = await definitionStore.ListAsync(new WorkflowDefinitionFilter(), cancellationToken);
        var references = await sourceReferenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: timeProvider.GetUtcNow(),
            cancellationToken);
        var unambiguousDefinitions = references
            .GroupBy(reference => reference.DefinitionId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. definitions
                .Where(definition => definition.DeletedAt is null && unambiguousDefinitions.Contains(definition.Id))
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ThenBy(definition => definition.Id, StringComparer.Ordinal)
                .Select(definition => new ActivityInputOption(
                    string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name,
                    definition.Id))
        ];
    }
}
