namespace Elsa.Agent.Core.Models;

// Shared by both journal backends. Native-source authority is verified by the runtime consumer.
internal static class AgentTurnApplicationRules
{
    internal static bool Bound(AgentTurnDocument document, AgentInvocationIdentity identity, string child) =>
        document.DeliveryProtocol == 0 ? document.Identity == identity && document.ChildInvocationId == child :
        document.Deliveries!.Any(delivery => delivery.Identity == identity && delivery.ChildInvocationId == child);

    internal static bool Admits(AgentTurnDocument document, AgentTurnApplication application) =>
        document.Status == AgentTurnStatus.Accepted && Bound(document, application.Identity, application.ChildInvocationId) &&
        (document.Applications?.SingleOrDefault(item => item.Identity == application.Identity) is not { } prior || prior == application);

    internal static AgentTurnDocument Apply(AgentTurnDocument document, AgentTurnApplication application) =>
        document.Applications?.Contains(application) == true ? document : document with { Applications = [.. document.Applications ?? [], application] };

    internal static void Validate(AgentTurnDocument document)
    {
        if (document.Applications is not { } applications) return;
        if (document.Status != AgentTurnStatus.Accepted || applications.Count is < 1 or > 64 ||
            applications.Select(item => item.Identity.ActivityExecutionId).Distinct(StringComparer.Ordinal).Count() != applications.Count)
            throw new InvalidDataException("The agent process-application record is invalid.");
        foreach (var item in applications)
        {
            ArgumentNullException.ThrowIfNull(item.Identity);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.ChildInvocationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.SourceId);
            if (item.AppliedAt == default || item.SourceId.Length > 2048 || !AgentTurnDocumentRules.IsToken(item.SourceFingerprint) ||
                !Bound(document, item.Identity, item.ChildInvocationId))
                throw new InvalidDataException("The agent process application belongs to another delivery or source.");
        }
    }
}
