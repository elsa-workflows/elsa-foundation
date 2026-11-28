using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost;

internal sealed class KernelPeerRouteGuard(
    IWorkflowExecutableSourceReferenceStore references,
    IWorkflowExecutableStore executables,
    KernelServing serving) : IWorkflowActivationRouteGuard
{
    public async ValueTask ValidateAsync(WorkflowActivationStamp candidate, IReadOnlyCollection<WorkflowActivationStamp> active,
        CancellationToken cancellationToken)
    {
        var (replacement, _) = await ReadAsync(candidate, cancellationToken);
        if (!replacement.Workflow.Nodes.Values.Any(node => node.Type == typeof(HttpEndpoint))) return;

        // The publishing node may not yet have seen another node's committed slot. Use the
        // transaction's view of each generation and the same route validator as ordinary serving.
        var publications = new List<KernelServedSlot> { replacement };
        foreach (var slot in active.Where(slot => slot.SlotId != candidate.SlotId))
        {
            var local = serving.Find(slot.SlotId);
            publications.Add(Matches(local, slot) ? local! : (await ReadAsync(slot, cancellationToken)).Served);
        }

        try { serving.PlanPeerPublications(publications); }
        catch (HttpRouteConflictException error)
        {
            throw new TriggerOperationException("route-conflict", error.Message);
        }
    }

    internal static bool Matches(KernelServedSlot? local, WorkflowActivationStamp slot) =>
        local is not null && local.Revision == slot.Revision && local.ActivationId == slot.ActivationId;

    internal async ValueTask<(KernelServedSlot Served, WorkflowExecutableSourceReference Source)> ReadAsync(
        WorkflowActivationStamp slot, CancellationToken cancellationToken)
    {
        var source = await references.FindAsync(WorkflowActivationReferenceIdentity.Create(slot.ActivationId), cancellationToken);
        if (source is null || source.SlotId != slot.SlotId || source.DefinitionId != slot.DefinitionId ||
            source.ActivationId != slot.ActivationId ||
            await executables.FindAsync(source.ArtifactId, cancellationToken) is not { } executable)
            throw new InvalidDataException($"Activation '{slot.ActivationId}' does not name an exact executable for slot '{slot.SlotId}'.");

        var workflow = NativeExecutableMapper.Map(executable, serving.Capabilities, slot.DefinitionId);
        return (new(slot.SlotId, slot.DefinitionId, slot.SlotName, slot.ActivationId, workflow, slot.Revision), source);
    }
}
