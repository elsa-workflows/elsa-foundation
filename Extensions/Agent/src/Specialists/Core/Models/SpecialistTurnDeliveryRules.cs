using Elsa.Agent.Specialists.Core.Exceptions;

namespace Elsa.Agent.Specialists.Core.Models;

internal static class SpecialistTurnDeliveryRules
{
    internal static bool SameLogical(SpecialistInvocationIdentity left, SpecialistInvocationIdentity right) =>
        (left with { ActivityExecutionId = right.ActivityExecutionId }) == right;

    internal static bool Admits(SpecialistTurnDocument document, SpecialistInvocationIdentity identity) =>
        document.Identity == identity || document.DeliveryProtocol == 1 && document.Deliveries!.Any(x => x.Identity == identity);

    internal static SpecialistTurnDocument Register(SpecialistTurnDocument document, SpecialistInvocationIdentity identity)
    {
        if (Admits(document, identity)) return document;
        if (document.DeliveryProtocol != 1 || !SameLogical(document.Identity, identity)) throw Conflict(document);
        if (document.Deliveries!.Count >= 64)
            throw new SpecialistJournalException("specialist.journal.capacity", document.Handle, "This operation has reached its bounded native delivery capacity.", new InvalidOperationException("The initial delivery profile permits at most64 parent associations."));
        return document with { Deliveries = [.. document.Deliveries, new(identity)] };
    }

    internal static bool CanBindChild(SpecialistTurnDocument document, SpecialistInvocationIdentity identity, string child)
    {
        if (string.IsNullOrWhiteSpace(child) || !Admits(document, identity)) return false;
        if (document.DeliveryProtocol == 0) return document.ChildInvocationId is null || document.ChildInvocationId == child;
        var delivery = document.Deliveries!.Single(x => x.Identity == identity);
        return (delivery.ChildInvocationId is null || delivery.ChildInvocationId == child) &&
            !document.Deliveries.Any(x => x.Identity != identity && x.ChildInvocationId == child);
    }

    internal static SpecialistTurnDocument BindChild(SpecialistTurnDocument document, SpecialistInvocationIdentity identity, string child)
    {
        if (!CanBindChild(document, identity, child)) throw Conflict(document);
        return document.DeliveryProtocol == 0 ? document : document with
        {
            Deliveries = document.Deliveries!.Select(x => x.Identity == identity ? x with { ChildInvocationId = child } : x).ToArray()
        };
    }

    internal static SpecialistInvocationIdentity DispatchIdentity(SpecialistTurnDocument document) => document.DeliveryProtocol == 0 ? document.Identity :
        document.Deliveries!.Single(x => x.Identity.ActivityExecutionId == document.ActiveDeliveryActivityId).Identity;

    internal static bool OwnsClaim(SpecialistTurnDocument document, SpecialistInvocationIdentity identity) =>
        Admits(document, identity) && (document.DeliveryProtocol == 0 || document.ActiveDeliveryActivityId == identity.ActivityExecutionId &&
            document.Deliveries!.Any(x => x.Identity == identity && x.ChildInvocationId == document.ChildInvocationId));

    internal static void Validate(SpecialistTurnDocument document)
    {
        if (document.DeliveryProtocol == 0)
        {
            if (document.Deliveries is not null || document.ActiveDeliveryActivityId is not null) throw new InvalidDataException("A legacy turn cannot carry delivery authority.");
            return;
        }
        if (document.DeliveryProtocol != 1 || document.Deliveries is not { Count: >= 1 and <= 64 } deliveries ||
            deliveries.Select(x => x.Identity.ActivityExecutionId).Distinct(StringComparer.Ordinal).Count() != deliveries.Count ||
            !deliveries.Any(x => x.Identity == document.Identity)) throw new InvalidDataException("The specialist delivery protocol is invalid.");
        var utf8 = new System.Text.UTF8Encoding(false, true);
        foreach (var delivery in deliveries)
        {
            if (!SameLogical(document.Identity, delivery.Identity)) throw new InvalidDataException("A native delivery belongs to another operation.");
            ArgumentException.ThrowIfNullOrWhiteSpace(delivery.Identity.ActivityExecutionId);
            _ = utf8.GetByteCount(delivery.Identity.ActivityExecutionId);
            if (delivery.ChildInvocationId is { } child) { ArgumentException.ThrowIfNullOrWhiteSpace(child); _ = utf8.GetByteCount(child); }
        }
        var children = deliveries.Where(x => x.ChildInvocationId is not null).Select(x => x.ChildInvocationId).ToArray();
        if (children.Distinct(StringComparer.Ordinal).Count() != children.Length) throw new InvalidDataException("A native child cannot belong to multiple deliveries.");
        if (document.Status == SpecialistTurnStatus.Prepared || document.Status == SpecialistTurnStatus.Cancelled && document.AdmissionGeneration == 0)
        {
            if (document.ActiveDeliveryActivityId is not null || children.Length != 0) throw new InvalidDataException("A staged operation has no dispatched delivery.");
        }
        else if (document.ActiveDeliveryActivityId is null || !deliveries.Any(x => x.Identity.ActivityExecutionId == document.ActiveDeliveryActivityId && x.ChildInvocationId == document.ChildInvocationId))
            throw new InvalidDataException("The physical dispatch has no exact delivery association.");
    }

    internal static SpecialistJournalException Conflict(SpecialistTurnDocument document) =>
        new("specialist.journal.conflict", document.Handle, "The native delivery does not match this specialist operation.", new InvalidOperationException("The native operation or delivery fingerprint changed."));
}
