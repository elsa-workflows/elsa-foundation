using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed class KernelStandaloneHttpResponseReader(PersistenceScope persistence, BoundScope actors,
    IRuntimeIngressDeliveryStore deliveries, IRuntimePreparedStandaloneRecipient target, Func<IPreparedRootStartHandoff> foundation)
    : IRuntimeStandaloneHttpResponseReader
{
    public async ValueTask<RuntimeStandaloneHttpResponse> ReadAsync(TriggerScope scope, string sourceOutboxItemId,
        CancellationToken cancellationToken)
    {
        if (scope.Persistence != persistence || (scope.TenantId ?? PersistenceScope.DefaultValue) != actors.Tenant)
            throw new InvalidOperationException("The HTTP delivery is outside this Foundation owner's scope.");
        var item = await deliveries.FindDeliveryAsync(scope, sourceOutboxItemId, cancellationToken)
            ?? throw new InvalidDataException("The accepted HTTP delivery source is unavailable.");
        if (item.StandaloneIngress is not { } source || !PreparedStandaloneHttp.IsWorkflowResponse(source) ||
            item.SubscriptionDelivery is not { Recipients: [var recipient], Progress: [var progress] })
            throw new InvalidDataException("The accepted delivery does not own one authored HTTP response.");
        if (progress.Seal is { Version: RuntimeDispatchSeal.CurrentVersion } ||
            progress.Seal is null && !target.Selects(source, recipient))
            return new(RuntimeStandaloneHttpResponseState.Legacy);
        if (!PreparedStandaloneHttp.HasVerifiedEmptyWaits(item))
            throw new NotSupportedException("The selected HTTP delivery has unqualified ordinary wait recipients.");
        if (progress.Phase == RuntimeRecipientPhase.CancelledBeforeDispatch)
            return new(RuntimeStandaloneHttpResponseState.Missing);
        if (item.Status == RuntimePostCommitOutboxStatus.FailedFinal &&
            progress is { Phase: RuntimeRecipientPhase.Pending, RecoveryState: RuntimeRecipientRecoveryState.BlockedCapability,
                LastFailureCode: KernelStandaloneHttpWaits.AuthorityConflict })
            throw new TriggerOperationException("conflict", "An incompatible Actor workflow wait blocks the accepted HTTP delivery.");
        if (progress is not { Seal: { Version: RuntimeDispatchSeal.PreparedStartQualificationVersion } seal,
                PreparedStartAttempt: { Disarmed: false } birth })
            return new(RuntimeStandaloneHttpResponseState.Pending);
        var actor = birth.Identity.ActorReference;
        if (birth.Identity.Tenant != actors.Tenant || progress.Receipt is { } receipt &&
            (receipt.Kind != RuntimeTargetAcceptanceKind.PreparedActorBirth || receipt.EvidenceId != actor))
            throw new InvalidDataException("The accepted delivery does not name the exact prepared actor.");

        // The provider proves the signed birth and this committed response together. A lost producer acknowledgement
        // does not delay a response whose authority is already physical and survives actor retirement.
        var response = await foundation().ReadResponseAsync(new(actor), birth.Identity.Issuance, seal.Fingerprint, cancellationToken);
        if (response is null) return new(RuntimeStandaloneHttpResponseState.Pending);
        if (!response.Present) return new(RuntimeStandaloneHttpResponseState.Missing);
        if (response.Payload is null) return new(RuntimeStandaloneHttpResponseState.TooLarge);
        var instruction = JsonSerializer.Deserialize<HttpResponse>(response.Payload);
        if (instruction is not { StatusCode: >= 100 and <= 599 })
            throw new InvalidDataException("The retained workflow response instruction is invalid.");
        return new(RuntimeStandaloneHttpResponseState.Ready, actor, instruction.StatusCode, instruction.ContentType,
            instruction.Body is null ? null : JsonSerializer.SerializeToElement(instruction.Body), instruction.Headers);
    }
}
