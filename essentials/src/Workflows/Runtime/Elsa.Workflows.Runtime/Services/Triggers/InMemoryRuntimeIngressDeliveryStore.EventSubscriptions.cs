using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed partial class InMemoryRuntimeIngressDeliveryStore
{
    public bool SupportsWorkflowCohort(string intentKind) => SupportsAdmission &&
        new StimulusDeliveryRequestProjector(stimulusAdapters ?? []).Owns(intentKind);

    public async ValueTask<PostCommitDeliveryContext> InstallWorkflowCohortAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot ordinary, RuntimeWorkflowSubscriptionCandidate subscriptions, CancellationToken cancellationToken = default)
    {
        try
        {

            Require(subscriptions.Scope, cancellationToken);
            var projector = new StimulusDeliveryRequestProjector(stimulusAdapters ?? []);
            var request = projector.Read(source);
            var expected = request.Mode == StimulusRoutingMode.ResumeOnly ? null : projector.SubscriptionSource(source);
            RuntimeWorkflowSubscriptionValidation.CheckCandidate(source, ordinary, subscriptions, expected);
            if (subscriptions.Delivery.Recipients.Any(recipient => recipient.ModelProtocol is not null))
                throw new NotSupportedException("The selected native provider cannot retain independent model starts.");
            _ = await ((InMemoryRuntimeCheckpointCommitStore)outbox).InstallCombinedAsync(source, ordinary, subscriptions,
                (InMemoryWorkflowActivationAuthority)slots, cancellationToken);
            return await ((IStimulusDeliveryStore)outbox).ReadSourceAsync(source, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(source.SourceOutboxItemId, exception);
        }
    }
}
