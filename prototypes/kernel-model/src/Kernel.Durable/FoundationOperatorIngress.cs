namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    internal enum OperatorAdmission { Accepted, Duplicate, Unavailable, StaleSelection }

    private static string FoundationOperatorKey(string tenant, string id) => tenant + "_" + id;

    internal (OperatorAdmission Outcome, string? Reference) AdmitFoundationOperator(string tenant, string? reference, string? callerKey,
        string id, string kind, string? payload, long? due, TimeSpan? keepFor, string? selection, string? replyTo, ParkRecoveryMode? recoverMode = null,
        IReadOnlyList<string>? recoverHandles = null)
    {
        lock (_gate)
        {
            if (callerKey is not null)
                reference = _foundationCallerBindings.GetValueOrDefault(tenant + "_" + callerKey)?.Reference;
            if (reference is null || !_referenceBindings.TryGetValue(reference, out var binding) || binding.Tenant != tenant ||
                selection is not null && BoundActor(tenant, reference) is null)
                return (OperatorAdmission.Unavailable, null);
            var resolvesPark = kind is "kernel.discard" or "kernel.recover";
            if (resolvesPark)
            {
                if (selection is null || !NamesFoundationPark(tenant, reference, selection))
                    return (OperatorAdmission.StaleSelection, null);
            }
            else if (selection is not null && !MatchesFoundationSelection(tenant, reference, selection))
                return (OperatorAdmission.StaleSelection, null);

            // The receipt is scoped to the operation, and keeps the first target after the Park's mark changes.
            if (_foundationOperatorReceipts.TryGetValue(FoundationOperatorKey(tenant, id), out var prior) && prior.Until >= clock())
                return (OperatorAdmission.Duplicate, prior.Reference);
            if (resolvesPark && !MatchesFoundationSelection(tenant, reference, selection))
                return (OperatorAdmission.StaleSelection, null);

            var command = new FoundationOperatorCommand(selection) { RecoverMode = recoverMode, RecoverHandles = recoverHandles };
            return AddFoundationMessage(tenant, reference, id, kind, payload, due, keepFor, replyTo: replyTo, @operator: command) switch
            {
                true => (OperatorAdmission.Accepted, reference),
                false => (OperatorAdmission.Duplicate, reference),
                null => (OperatorAdmission.Unavailable, null)
            };
        }
    }
}
