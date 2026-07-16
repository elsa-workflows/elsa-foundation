using Elsa.Persistence.Groundwork.Serialization;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Shared serializer instances for store tests. Stores now depend on
/// <see cref="IGroundworkRuntimeDocumentSerializer"/>; tests that construct a store directly pass
/// <see cref="Serializer"/>, which is the production default wired to the production migration chain.
/// </summary>
internal static class GroundworkTestSerialization
{
    /// <summary>The default upcaster registry with the production runtime migration chain.</summary>
    public static readonly IGroundworkRuntimeDocumentUpcasterRegistry UpcasterRegistry =
        new GroundworkRuntimeDocumentUpcasterRegistry(
        [
            new ExecutionScopeAttemptDocumentUpcaster(ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind),
            new ExecutionScopeAttemptDocumentUpcaster(ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind),
            new ExecutionScopeAttemptDocumentUpcaster(ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind),
            new WorkflowExecutionStateDocumentV1ToV2Upcaster(),
            new WorkflowExecutionStateDocumentV2ToV3Upcaster(),
            new WorkflowTriggerBindingDocumentV1ToV2Upcaster(),
            new RecurringTriggerScheduleDocumentV1ToV2Upcaster()
        ]);

    /// <summary>The production default serializer, wired to the production upcaster registry.</summary>
    public static readonly IGroundworkRuntimeDocumentSerializer Serializer =
        new GroundworkRuntimeDocumentSerializer(UpcasterRegistry);
}
