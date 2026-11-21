namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    internal enum OperatorAdmission { Accepted, Duplicate, Unavailable, StaleSelection }

    internal (OperatorAdmission Outcome, string? Reference) AdmitFoundationOperator(string tenant, string? reference, string? callerKey,
        string id, string kind, string? payload, long? due, TimeSpan? keepFor, string? selection, string? replyTo)
    {
        lock (_gate)
        {
            if (callerKey is not null)
                reference = _foundationCallerBindings.GetValueOrDefault(tenant + "_" + callerKey)?.Reference;
            if (selection is not null)
            {
                if (BoundActor(tenant, reference) is null) return (OperatorAdmission.Unavailable, null);
                if (!MatchesFoundationSelection(tenant, reference, selection))
                    return (OperatorAdmission.StaleSelection, null);
            }

            return AddFoundationMessage(tenant, reference, id, kind, payload, due, keepFor, replyTo: replyTo, @operator: new(selection)) switch
            {
                true => (OperatorAdmission.Accepted, reference),
                false => (OperatorAdmission.Duplicate, reference),
                null => (OperatorAdmission.Unavailable, null)
            };
        }
    }
}
