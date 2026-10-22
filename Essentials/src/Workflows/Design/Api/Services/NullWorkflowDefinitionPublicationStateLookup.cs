using Elsa.Workflows.Design.Persistence.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// Design-only default: returns no states so definition views leave <c>publicationState</c> unset.
/// NativeEndpoints' generated <c>Map()</c> resolves every constructor argument with
/// <c>GetRequiredService</c>; registering this keeps those hosts from failing when Publishing is absent.
/// Publishing replaces this registration.
/// </summary>
public sealed class NullWorkflowDefinitionPublicationStateLookup : IWorkflowDefinitionPublicationStateLookup
{
    public Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>>(
            new Dictionary<string, WorkflowDefinitionPublicationSummary>(StringComparer.Ordinal));
}
