using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Capabilities;

/// <summary>History links require a reader paired with the actual selected atomic writer.</summary>
public sealed class RuntimeHistoryCapabilitySource(IRuntimeCheckpointCommitStore? writer = null, IRuntimeHistoryStore? history = null)
    : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var supported = false;
        try { supported = history is not null && writer is { SupportsHistory: true } && writer.CanCommitHistory(history); }
        catch (InvalidOperationException) { /* No ordinary persistence scope is not a history-capable request. */ }
        catch (NotSupportedException) { /* The actual provider has no coherent history boundary. */ }
        if (!supported) return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([]);
        var links = new List<ApiCapabilityLink>
        {
            new("workflow-history-page", "runtime/workflows/instances/{workflowExecutionId}/history", templated: true),
            new("workflow-history-bookmarks", "runtime/workflows/instances/{workflowExecutionId}/history/bookmarks", templated: true)
        };
        if (history is IRuntimeHistoryOccurrenceStore)
            links.Add(new("workflow-history-occurrences", "runtime/workflows/instances/{workflowExecutionId}/history/occurrences", templated: true));
        if (history is IRuntimeHistoryDetailsStore)
        {
            links.Add(new("workflow-history-event", "runtime/workflows/instances/{workflowExecutionId}/history/events/{historyEventId}", templated: true));
            links.Add(new("workflow-history-bookmark", "runtime/workflows/instances/{workflowExecutionId}/history/bookmarks/{bookmarkId}", templated: true));
        }
        return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(
            [new(RuntimeApiCapabilities.CapabilityId, 1, links, $"{RuntimeApiCapabilities.SourceFeatureId}.History")]);
    }
}
