using System.Security.Cryptography;
using System.Text.Json;
using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

internal sealed record MigrationDispatchBatch(long Number, string From, string To, ActorRef[] Actors);
internal sealed record MigrationRequest(string From, string To, ActorRef Plan);
internal sealed record MigrationDispatchReceipt(int Accepted, int Duplicate, int Unavailable);

/// <summary>Reads committed plan state and acknowledges dispatch through a host-provided control path.</summary>
internal interface IMigrationDispatchState
{
    ValueTask<MigrationDispatchBatch?> ReadPendingAsync(BoundScope scope, ActorRef plan, CancellationToken cancellationToken);
    ValueTask AcknowledgeAsync(BoundScope scope, ActorRef plan, long batch, string operationId, CancellationToken cancellationToken);
}

/// <summary>Runs outside actor turns. Each retry starts with the same committed batch and operation IDs.</summary>
internal sealed class MigrationPlanDispatch(IIngress ingress, IMigrationDispatchState state)
{
    internal async Task<MigrationDispatchReceipt?> DispatchAsync(BoundScope scope, ActorRef plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var batch = await state.ReadPendingAsync(scope, plan, cancellationToken);
        if (batch is null) return null;
        if (batch.Actors is not { Length: > 0 } || batch.Actors.Any(actor => string.IsNullOrEmpty(actor.Id)))
            throw new InvalidOperationException("A committed migration batch requires nonempty exact actor references.");
        var payload = JsonSerializer.Serialize(new MigrationRequest(batch.From, batch.To, plan));
        var accepted = 0;
        var duplicate = 0;
        var unavailable = 0;
        foreach (var actor in batch.Actors)
        {
            var id = OperationId("migrate", plan, batch.Number, actor);
            var admission = await ingress.SendAsync(scope, new Target.Ref(actor), id, Kinds.Migrate,
                payload, cancellationToken: cancellationToken);
            switch (admission)
            {
                case { Outcome: AdmissionOutcome.Accepted, Actor: var exact } when exact == actor:
                    accepted++;
                    break;
                case { Outcome: AdmissionOutcome.Duplicate, Actor: var exact } when exact == actor:
                    duplicate++;
                    break;
                case { Outcome: AdmissionOutcome.Refused, Reason: RefusalReason.TargetMismatch }:
                    unavailable++;
                    break;
                default:
                    throw new InvalidOperationException($"Migration batch {batch.Number} could not dispatch a selected actor ({admission.Outcome}, {admission.Reason}).");
            }
        }
        await state.AcknowledgeAsync(scope, plan, batch.Number, OperationId("batch-dispatched", plan, batch.Number), cancellationToken);
        return new(accepted, duplicate, unavailable);
    }

    private static string OperationId(string purpose, ActorRef plan, long batch, ActorRef? target = null)
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(new { Purpose = purpose, Plan = plan.Id, Batch = batch, Actor = target?.Id });
        return "migration-" + Convert.ToHexString(SHA256.HashData(input));
    }
}
