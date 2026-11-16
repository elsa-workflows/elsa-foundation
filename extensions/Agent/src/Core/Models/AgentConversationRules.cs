namespace Elsa.Agent.Core.Models;

// One transition definition for the process-local and relational journals. A null transition refuses
// the entire turn mutation; adapters must never commit only one side of this pair.
internal static class AgentConversationRules
{
    internal static AgentConversationDocument? Change(AgentTurnDocument before, AgentTurnDocument next, AgentConversationDocument? current)
    {
        var admission = before.Conversation!;
        var owner = AgentConversationOwner.FromInvocation(before.Identity);
        if (next.Status == AgentTurnStatus.Claimed && before.Status == AgentTurnStatus.Prepared)
        {
            if (current is null)
            {
                if (admission.ExpectedRevision != 0) return null;
                current = new(admission.ConversationId, owner, 0, null, null, false, null);
            }
            else if (admission.ExpectedRevision == 0) return null;
            if (current.Owner != owner || current.Revision != admission.ExpectedRevision || current.OutcomeUnknown || current.ActiveTurnHandle is not null) return null;
            return current with { ActiveTurnHandle = before.Handle, ActiveFence = next.Fence, ActiveAdmissionGeneration = next.AdmissionGeneration };
        }
        if (current is null || current.Owner != owner || current.Revision != admission.ExpectedRevision || current.OutcomeUnknown && next.Status != AgentTurnStatus.Cancelled ||
            current.ActiveTurnHandle != before.Handle || current.ActiveFence != before.Fence || current.ActiveAdmissionGeneration != before.AdmissionGeneration) return null;
        if (before.Status == AgentTurnStatus.Claimed && next.Status == AgentTurnStatus.Claimed && next.AdmissionGeneration == before.AdmissionGeneration + 1)
            return current with { ActiveFence = next.Fence, ActiveAdmissionGeneration = next.AdmissionGeneration };
        return next.Status switch
        {
            AgentTurnStatus.Accepted => current with { Revision = checked(current.Revision + 1), ActiveTurnHandle = null, ActiveFence = null, ActiveAdmissionGeneration = 0,
                AcceptedTurn = new(before.Handle, AgentTurnDeliveryRules.DispatchIdentity(before), before.ChildInvocationId!) },
            AgentTurnStatus.Cancelled => current with { ActiveTurnHandle = null, ActiveFence = null, ActiveAdmissionGeneration = 0, OutcomeUnknown = false },
            AgentTurnStatus.Failed => current with { ActiveTurnHandle = null, ActiveFence = null, ActiveAdmissionGeneration = 0 },
            AgentTurnStatus.OutcomeUnknown => current with { OutcomeUnknown = true },
            _ => current
        };
    }

    internal static void Validate(AgentConversationDocument document)
    {
        AgentTurnDocumentRules.Token(document.Id);
        ValidateOwner(document.Owner);
        if (document.Revision < 0 || (document.Revision == 0) != (document.AcceptedTurn is null) ||
            (document.ActiveTurnHandle is null) != (document.ActiveFence is null) || document.OutcomeUnknown && document.ActiveTurnHandle is null ||
            document.ActiveAdmissionGeneration < 0 || document.ActiveTurnHandle is null && document.ActiveAdmissionGeneration != 0)
            throw new InvalidDataException("The agent conversation lifecycle is invalid.");
        if (document.ActiveTurnHandle is { } active) { AgentTurnDocumentRules.Token(active); AgentTurnDocumentRules.Fence(document.ActiveFence!); }
        if (document.AcceptedTurn is { } accepted)
        {
            AgentTurnDocumentRules.Token(accepted.Handle);
            if (document.Owner != AgentConversationOwner.FromInvocation(accepted.Identity) || string.IsNullOrWhiteSpace(accepted.ChildInvocationId))
                throw new InvalidDataException("The accepted agent conversation reference has another owner.");
        }
    }

    internal static void ValidateOwner(AgentConversationOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var utf8 = new System.Text.UTF8Encoding(false, true);
        foreach (var value in new[] { owner.PersistencePartition, owner.WorkflowExecutionId, owner.DefinitionVersionId, owner.TemplateHash })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _ = utf8.GetByteCount(value);
        }
        if (owner.TenantId is { } tenant)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
            _ = utf8.GetByteCount(tenant);
        }
    }
}
