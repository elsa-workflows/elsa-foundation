using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

namespace Kernel.WorkflowActor;

/// <summary>Workflow admission and operator operations through the public foundation contract.</summary>
public sealed class WorkflowRuntime(IIngress ingress, IActorReads reads)
{
    public ValueTask<Admission> StartAsync(BoundScope scope, string operationId, WorkflowStart workflow,
        string? callerKey = null, CancellationToken cancellationToken = default)
    {
        var spec = WorkflowActor.Start(workflow);
        Target target = callerKey is null ? new Target.Start(spec) : new Target.Key(callerKey, spec);
        return ingress.SendAsync(scope, target, operationId, Kinds.Start, cancellationToken: cancellationToken);
    }

    public ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default) =>
        reads.ListAsync(scope, query with { Type = WorkflowActor.ActorType }, from, cancellationToken);

    public ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef workflow, PageToken? from = null,
        CancellationToken cancellationToken = default) =>
        reads.InboxAsync(scope, workflow, from, cancellationToken);

    public ValueTask<Admission> TerminateAsync(BoundScope scope, string operationId, ActorView selected,
        string? reason = null, CancellationToken cancellationToken = default)
    {
        if (selected.Header.Type != WorkflowActor.ActorType || selected.Selection is not { } selection)
            return new(new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.StaleSelection));

        var command = new OperatorCommand(selection, Reason: reason);
        return ingress.SendAsync(scope, new Target.Ref(selected.Header.Ref), operationId, Kinds.Terminate,
            JsonSerializer.Serialize(command), cancellationToken: cancellationToken);
    }
}
