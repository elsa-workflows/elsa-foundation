using Kernel.Runtime;
using System.Security.Cryptography;

namespace Kernel.Durable;

internal static class FoundationIngressAddress
{
    internal static string Operation(string tenant, string id) => tenant + "_" + id;

    internal static Message Occurrence(string operation, string receipt, string occurrence, InstanceRef actor, long creation,
        string kind, string? payload, long? due, long until, string? replyTo, FoundationOperatorCommand? command) =>
        new Message(operation, actor, kind, payload, Due: due, KeepUntil: until, ReplyTo: replyTo, TargetCreation: creation)
            { StorageId = receipt + "/" + occurrence, Operator = command }.WithInboxScope(InboxKey.FoundationOccurrenceScope);
}

public sealed partial class InMemoryStore
{
    internal enum OperatorAdmission { Accepted, Duplicate, Unavailable, StaleSelection, TargetMismatch }

    internal bool MatchesThenDeleteTarget(InstanceRow row, Message message)
    {
        lock (_gate)
            return message is { Kind: Kinds.Terminate, Operator.DeleteTarget: { } expected } &&
                message.Target.Id == row.Id && message.TargetCreation == row.Creation &&
                _referenceBirths.TryGetValue(new(row.Id, row.Creation), out var binding) && binding.Reference == expected;
    }

    private bool IsThenDeleteRetirement(TurnCommit turn, InstanceRow? previous, long now)
    {
        if (!turn.Delete || previous is not { Retired: false }) return false;
        if (previous.ParkDiscard is { Kind: ParkClearKind.Terminate, ThenDelete: true } drain)
            return turn.Consumed.Contains(drain.Control);
        if (previous.ParkDiscard is not null || previous.ParkRecovery is not null ||
            _foundationParks.ContainsKey(new(previous.Id, previous.Creation))) return false;

        return turn.Consumed.Concat(turn.Done).Any(key =>
            Messages.GetValueOrDefault(key) is { Done: false, Message: { } source } && MatchesThenDeleteTarget(previous, source) &&
            !(source.Due > now) &&
            (source.Operator!.Selection is null || MatchesFoundationSelection(FoundationIdentity(previous.Id, previous.Creation).Tenant,
                source.Operator.DeleteTarget, source.Operator.Selection)));
    }

    internal (OperatorAdmission Outcome, string? Reference) AdmitRetentionDelete(string tenant, string reference, string selection,
        long? due = null, string? replyTo = null) =>
        AdmitFoundationOperator(tenant, reference, null, "", Kinds.Delete, null, due, null, selection, replyTo, retentionDelete: true);

    internal (OperatorAdmission Outcome, string? Reference) AdmitFoundationOperator(string tenant, string? reference, string? callerKey,
        string id, string kind, string? payload, long? due, TimeSpan? keepFor, string? selection, string? replyTo, ParkRecoveryMode? recoverMode = null,
        IReadOnlyList<string>? recoverHandles = null, string? deleteTarget = null, bool retentionDelete = false)
    {
        lock (_gate)
        {
            if (callerKey is not null)
                reference = _foundationCallerBindings.GetValueOrDefault(tenant + "_" + callerKey)?.Reference;
            if (reference is null || !_referenceBindings.TryGetValue(reference, out var binding) || binding.Tenant != tenant ||
                selection is not null && BoundActor(tenant, reference) is null)
                return (OperatorAdmission.Unavailable, null);
            if (deleteTarget is not null && (kind != Kinds.Terminate || !StringComparer.Ordinal.Equals(reference, deleteTarget)))
                return (OperatorAdmission.TargetMismatch, null);
            var resolvesPark = kind is "kernel.discard" or "kernel.recover";
            if (resolvesPark)
            {
                if (selection is null || !NamesFoundationPark(tenant, reference, selection))
                    return (OperatorAdmission.StaleSelection, null);
            }
            else if (deleteTarget is null && (kind == Kinds.Delete || selection is not null))
            {
                Span<byte> mark = stackalloc byte[SelectionBytes];
                if (!TryMatchFoundationSelection(tenant, reference, selection, mark, requireTerminal: kind == Kinds.Delete))
                    return (OperatorAdmission.StaleSelection, null);
                if (retentionDelete)
                {
                    // A fresh read seals the same terminal mark with a different nonce.
                    var identity = Json.Write(new { tenant, reference, Mark = Convert.ToHexString(mark) });
                    id = "~retention-" + Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(identity)));
                }
            }

            // The receipt is scoped to the operation, and keeps the first target after the Park's mark changes.
            if (_foundationOperatorReceipts.TryGetValue(FoundationIngressAddress.Operation(tenant, id), out var prior) && prior.Until >= clock())
                return (OperatorAdmission.Duplicate, prior.Reference);
            if (kind == Kinds.Terminate && selection is null && deleteTarget is null &&
                BoundActor(tenant, reference)?.Row.Status != InstanceStatus.Running)
                return (OperatorAdmission.StaleSelection, null);
            if (resolvesPark && !MatchesFoundationSelection(tenant, reference, selection))
                return (OperatorAdmission.StaleSelection, null);

            var command = new FoundationOperatorCommand(selection)
            {
                RecoverMode = recoverMode, RecoverHandles = recoverHandles, DeleteTarget = deleteTarget, RetentionDelete = retentionDelete
            };
            return AddFoundationMessage(tenant, reference, id, kind, payload, due, keepFor, keyed: retentionDelete, replyTo: replyTo, @operator: command) switch
            {
                true => (OperatorAdmission.Accepted, reference),
                false => (OperatorAdmission.Duplicate, reference),
                null => (OperatorAdmission.Unavailable, null)
            };
        }
    }
}
