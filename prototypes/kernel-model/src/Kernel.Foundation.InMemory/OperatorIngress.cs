using System.Text.Json;
using Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

internal sealed class OperatorIngress(Durable.InMemoryStore store, Func<BoundScope, string, bool>? authorize)
{
    public ValueTask<Admission> SendAsync(BoundScope scope, Target target, string id, string kind, string? payload,
        AddOptions? options, CancellationToken cancellationToken, string? replyTo)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var refused = new Admission(AdmissionOutcome.Refused);
        if (authorize?.Invoke(scope, kind) != true || !Durable.InMemoryStore.IsOutsideMessageId(id) ||
            kind == Kinds.Recover)
            return new(refused);

        string? selection = null;
        if (kind is Kinds.Terminate or Kinds.Delete or Kinds.Discard)
        {
            OperatorCommand? command;
            try { command = payload is null ? null : JsonSerializer.Deserialize<OperatorCommand>(payload); }
            catch (JsonException) { return new(refused); }
            if (command?.ThenDelete == true) return new(refused);
            if (command is not { Selection.Length: > 0 })
                return new(new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.StaleSelection));
            selection = command.Selection;
        }

        var receipt = store.AdmitFoundationOperator(scope.Tenant, (target as Target.Ref)?.Actor.Id,
            (target as Target.Key)?.CallerKey, id, kind, payload, options?.DueAt, options?.KeepFor, selection, replyTo);
        return new(receipt.Outcome switch
        {
            Durable.InMemoryStore.OperatorAdmission.Accepted => new Admission(AdmissionOutcome.Accepted, new ActorRef(receipt.Reference!)),
            Durable.InMemoryStore.OperatorAdmission.Duplicate => new Admission(AdmissionOutcome.Duplicate, new ActorRef(receipt.Reference!)),
            Durable.InMemoryStore.OperatorAdmission.StaleSelection => new Admission(AdmissionOutcome.Refused, Reason: RefusalReason.StaleSelection),
            _ => refused
        });
    }
}
