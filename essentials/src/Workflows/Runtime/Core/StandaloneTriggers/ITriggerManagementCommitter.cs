namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Replacement contract: commits a prepared authorized resource mutation with its physical source projections.</summary>
/// <remarks>The default authoring adapter refuses enabling or changing an activated resource. Source-aware providers
/// must compare resource, route/schedule and target observations at their actual protected commit boundary.</remarks>
public interface ITriggerManagementCommitter
{
    bool SupportsActivatedResources => false;
    /// <summary>Actual installed source/schema plus protected physical activation support; authoring metadata is insufficient.</summary>
    bool CanActivateType(string typeKey, int schemaVersion) => false;
    /// <summary>Protected stop-only updates remain available while the actual projection planner is installed.</summary>
    bool CanStopType(string typeKey, int schemaVersion) => false;
    ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write,
        CancellationToken cancellationToken = default);
}
