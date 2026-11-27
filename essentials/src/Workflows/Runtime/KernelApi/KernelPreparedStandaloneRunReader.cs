using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.KernelHost;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelPreparedStandaloneRunReader(KernelRunReader reader, KernelRuns runs) : IRuntimePreparedStandaloneRunReader
{
    public async ValueTask<WorkflowExecutionState?> ReadAsync(TriggerScope scope, RuntimeSubscriptionRecipient recipient,
        RuntimeTargetAcceptanceReceipt receipt, CancellationToken cancellationToken)
    {
        if (receipt.Kind != RuntimeTargetAcceptanceKind.PreparedActorBirth || scope.Persistence.Value != runs.Tenant.Tenant ||
            (scope.TenantId ?? PersistenceScope.DefaultValue) != runs.Tenant.Tenant || recipient.TenantId != scope.TenantId ||
            receipt.RecipientId != recipient.RecipientId || receipt.Material != recipient.Material)
            return null;
        var run = await reader.GetAsync(receipt.EvidenceId, cancellationToken);
        return run is { Header: { TestStart: null, Ephemeral: false }, View.Header.Parent: null } &&
            run.Header.Definition == recipient.LogicalDefinitionId && run.Executable?.Identity == recipient.Material
                ? run.State with { PinnedSource = recipient.SourceProvenance, Partition = recipient.Partition, Authority = recipient.Authority }
                : null;
    }
}
