using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore
{
    public ValueTask<PostCommitDeliveryContext> InstallWorkflowCohortAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot ordinary, RuntimeWorkflowSubscriptionCandidate subscriptions, CancellationToken cancellationToken = default) =>
        EfTriggerBoundary.RunAsync(() => InstallWorkflowCohortAsyncCore(source, ordinary, subscriptions, cancellationToken), source?.Scope.Value, source?.Claim.Item.OutboxItemId);

    public bool SupportsWorkflowCohort(string intentKind) => !PreparedStandaloneOnly && SupportsAdmission &&
        legacy?.SupportsWorkflowCohort(intentKind) == true;

    private ValueTask<PostCommitDeliveryContext> InstallWorkflowCohortAsyncCore(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot ordinary, RuntimeWorkflowSubscriptionCandidate subscriptions, CancellationToken cancellationToken = default)
    {
        Require(subscriptions.Scope, cancellationToken); RequireOwnedBoundary();
        return RequireLegacy().InstallWorkflowCohortAsync(source, ordinary, subscriptions, cancellationToken);
    }
}
