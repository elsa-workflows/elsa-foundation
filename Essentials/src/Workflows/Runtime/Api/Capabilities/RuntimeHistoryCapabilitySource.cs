using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Capabilities;

/// <summary>History links require a reader paired with the actual selected atomic writer.</summary>
public sealed class RuntimeHistoryCapabilitySource(IRuntimeCheckpointCommitStore writer, IRuntimeHistoryStore? history = null)
    : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var supported = false;
        try { supported = history is not null && writer.SupportsHistory && writer.CanCommitHistory(history); }
        catch (InvalidOperationException) { /* No ordinary persistence scope is not a history-capable request. */ }
        catch (NotSupportedException) { /* The actual provider has no coherent history boundary. */ }
        return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(supported
            ? [new(RuntimeApiCapabilities.CapabilityId, 1,
                [
                    new ApiCapabilityLink("workflow-history-page", "runtime/workflows/instances/{workflowExecutionId}/history", templated: true),
                    new ApiCapabilityLink("workflow-history-event", "runtime/workflows/instances/{workflowExecutionId}/history/events/{historyEventId}", templated: true),
                    new ApiCapabilityLink("workflow-history-bookmarks", "runtime/workflows/instances/{workflowExecutionId}/history/bookmarks", templated: true),
                    new ApiCapabilityLink("workflow-history-bookmark", "runtime/workflows/instances/{workflowExecutionId}/history/bookmarks/{bookmarkId}", templated: true)
                ], $"{RuntimeApiCapabilities.SourceFeatureId}.History")]
            : []);
    }
}
