using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Physical evidence for the classic ingress arms, selected with the classic EF provider.</summary>
internal interface IEfLegacyRuntimeIngress
{
    IWorkflowExecutionStateStore Executions { get; }
    bool OwnsStartEvidence(RuntimeDbContext expectedContext, IStandaloneTriggerStore expectedResources,
        IRuntimePostCommitOutboxStore expectedOutbox, IWorkflowExecutableStore expectedExecutables,
        IPersistenceAccessContextAccessor expectedAccess, TimeProvider expectedTime);
    bool OwnsModelInputReader { get; }
    void ValidateDispatch(RuntimeCommittedRecipient target, WorkflowExecutable? native);
    ValueTask<RuntimeTargetAcceptanceReceipt?> ObserveDispatchAsync(RuntimeCommittedRecipient target, CancellationToken token);
    void RequireWaitBoundary();
    ValueTask<RuntimeIngressWaitDelivery> CaptureWaitsAsync(RuntimeIngressAdmissionRequest request, CancellationToken token);
    ValueTask FenceWaitsAsync(RuntimeIngressAdmissionRequest request, RuntimeIngressWaitDelivery expected, CancellationToken token);
    ValueTask<RuntimeIngressWaitReceipt?> ObserveWaitAsync(RuntimeCommittedIngressWait target, CancellationToken token);
    bool SupportsWorkflowCohort(string intentKind);
    ValueTask<PostCommitDeliveryContext> InstallWorkflowCohortAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot ordinary, RuntimeWorkflowSubscriptionCandidate subscriptions, CancellationToken token);
}
