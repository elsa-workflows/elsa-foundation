using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore
{
    public bool SupportsWorkflowCohort(string intentKind) => SupportsAdmission &&
        new StimulusDeliveryRequestProjector(stimulusAdapters ?? []).Owns(intentKind);

    public async ValueTask<PostCommitDeliveryContext> InstallWorkflowCohortAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot ordinary, RuntimeWorkflowSubscriptionCandidate subscriptions, CancellationToken cancellationToken = default)
    {
        Require(subscriptions.Scope, cancellationToken); RequireOwnedBoundary();
        var projector = new StimulusDeliveryRequestProjector(stimulusAdapters ?? []);
        var request = projector.Read(source);
        var expected = request.Mode == StimulusRoutingMode.ResumeOnly ? null : projector.SubscriptionSource(source);
        RuntimeWorkflowSubscriptionValidation.CheckCandidate(source, ordinary, subscriptions, expected);
        if (subscriptions.Delivery.Recipients.Any(recipient => recipient.ModelProtocol is not null) &&
            (modelInputs is not EfRuntimeModelExecutionReader reader || !reader.Uses(context, access)))
            throw new NotSupportedException("Independent model starts require the actual selected model receipt boundary.");
        _ = await ((EfRuntimePostCommitOutboxStore)outbox).InstallCombinedAsync(source, ordinary, subscriptions, resources, cancellationToken);
        return await ((IStimulusDeliveryStore)outbox).ReadSourceAsync(source, cancellationToken);
    }
}
