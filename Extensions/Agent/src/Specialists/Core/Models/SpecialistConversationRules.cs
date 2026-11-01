namespace Elsa.Agent.Specialists.Core.Models;

// One transition definition for the process-local and relational journals. A null transition refuses
// the entire turn mutation; adapters must never commit only one side of this pair.
internal static class SpecialistConversationRules
{
    internal static SpecialistConversationDocument? Change(SpecialistTurnDocument before, SpecialistTurnDocument next, SpecialistConversationDocument? current)
    {
        var admission = before.Conversation!;
        var owner = SpecialistConversationOwner.FromInvocation(before.Identity);
        if (next.Status == SpecialistTurnStatus.Claimed && before.Status == SpecialistTurnStatus.Prepared)
        {
            if (current is null)
            {
                if (admission.ExpectedRevision != 0) return null;
                current = new(admission.ConversationId, owner, 0, null, null, false, null);
            }
            else if (admission.ExpectedRevision == 0) return null;
            if (current.Owner != owner || current.Revision != admission.ExpectedRevision || current.OutcomeUnknown || current.ActiveTurnHandle is not null) return null;
            return current with { ActiveTurnHandle = before.Handle, ActiveFence = next.Fence };
        }
        if (current is null || current.Owner != owner || current.Revision != admission.ExpectedRevision || current.OutcomeUnknown ||
            current.ActiveTurnHandle != before.Handle || current.ActiveFence != before.Fence) return null;
        return next.Status switch
        {
            SpecialistTurnStatus.Accepted => current with { Revision = checked(current.Revision + 1), ActiveTurnHandle = null, ActiveFence = null,
                AcceptedTurn = new(before.Handle, before.Identity, before.ChildInvocationId!) },
            SpecialistTurnStatus.Failed => current with { ActiveTurnHandle = null, ActiveFence = null },
            SpecialistTurnStatus.OutcomeUnknown => current with { OutcomeUnknown = true },
            _ => current
        };
    }

    internal static void Validate(SpecialistConversationDocument document)
    {
        SpecialistTurnDocumentRules.Token(document.Id);
        ArgumentNullException.ThrowIfNull(document.Owner);
        foreach (var value in new[] { document.Owner.PersistencePartition, document.Owner.WorkflowExecutionId, document.Owner.DefinitionVersionId, document.Owner.TemplateHash })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _ = new System.Text.UTF8Encoding(false, true).GetByteCount(value);
        }
        if (document.Owner.TenantId is { } tenant)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
            _ = new System.Text.UTF8Encoding(false, true).GetByteCount(tenant);
        }
        if (document.Revision < 0 || (document.Revision == 0) != (document.AcceptedTurn is null) ||
            (document.ActiveTurnHandle is null) != (document.ActiveFence is null) || document.OutcomeUnknown && document.ActiveTurnHandle is null)
            throw new InvalidDataException("The specialist conversation lifecycle is invalid.");
        if (document.ActiveTurnHandle is { } active) { SpecialistTurnDocumentRules.Token(active); SpecialistTurnDocumentRules.Fence(document.ActiveFence!); }
        if (document.AcceptedTurn is { } accepted)
        {
            SpecialistTurnDocumentRules.Token(accepted.Handle);
            if (document.Owner != SpecialistConversationOwner.FromInvocation(accepted.Identity) || string.IsNullOrWhiteSpace(accepted.ChildInvocationId))
                throw new InvalidDataException("The accepted specialist conversation reference has another owner.");
        }
    }
}
