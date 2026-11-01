using System.Text;
using System.Text.Json.Serialization;
using Elsa.Agent.Specialists.Core.Exceptions;

namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>One exact native parent's cancellation notification. Source facts grant no authority by themselves.</summary>
public sealed record SpecialistTurnCancellation(SpecialistInvocationIdentity Identity, string? ChildInvocationId,
    string SourceId, string SourceFingerprint, DateTimeOffset CancelledAt);

/// <summary>The journal decision, separate from the native process's cancellation and provider interruption.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpecialistCancellationOutcome>))]
public enum SpecialistCancellationOutcome
{
    /// <summary>A tombstone blocks delayed admission of this native parent.</summary>
    CancelledBeforeAdmission,
    /// <summary>Cancellation won against this parent's unaccepted turn.</summary>
    Cancelled,
    /// <summary>The immutable accepted result/history won first.</summary>
    AlreadyAccepted,
    /// <summary>The turn already has a rejected/cancelled terminal decision.</summary>
    AlreadyTerminal,
    /// <summary>This parent's cancellation does not change another native delivery's work.</summary>
    OtherDeliveryUnchanged
}

/// <summary>Retained canonical cancellation decision and the target's observed claim generation, if any.</summary>
public sealed record SpecialistCancellationReceipt(SpecialistTurnCancellation Cancellation, SpecialistCancellationOutcome Outcome, int? AdmissionGeneration);

internal sealed record SpecialistOperationDocument(SpecialistInvocationIdentity Identity, IReadOnlyList<SpecialistCancellationReceipt> Cancellations);

// Shared by Memory and EF. An operation gate serializes admission/takeover with pre-admission cancellation.
internal static class SpecialistOperationRules
{
    internal static void Validate(SpecialistOperationDocument operation)
    {
        Identity(operation.Identity);
        if (operation.Cancellations.Count > 64 || operation.Cancellations.Select(item => item.Cancellation.Identity.ActivityExecutionId).Distinct(StringComparer.Ordinal).Count() != operation.Cancellations.Count)
            throw new InvalidDataException("The specialist operation cancellation capacity or identity is invalid.");
        foreach (var receipt in operation.Cancellations)
        {
            Cancellation(receipt.Cancellation);
            if (!SpecialistTurnDeliveryRules.SameLogical(operation.Identity, receipt.Cancellation.Identity) || !Enum.IsDefined(receipt.Outcome) || receipt.AdmissionGeneration is < 0)
                throw new InvalidDataException("The specialist cancellation receipt belongs to another operation.");
        }
    }

    internal static void Identity(SpecialistInvocationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _ = SpecialistTurnDocument.CalculateOperationKey(identity);
        var utf8 = new UTF8Encoding(false, true);
        foreach (var value in new[] { identity.ActivityExecutionId, identity.ArtifactId, identity.ArtifactHash, identity.DefinitionVersionId, identity.TemplateHash })
        { ArgumentException.ThrowIfNullOrWhiteSpace(value); _ = utf8.GetByteCount(value); }
    }

    internal static void Cancellation(SpecialistTurnCancellation cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        Identity(cancellation.Identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancellation.SourceId);
        if (cancellation.CancelledAt == default || cancellation.SourceId.Length > 2048 || !SpecialistTurnDocumentRules.IsToken(cancellation.SourceFingerprint))
            throw new InvalidDataException("The specialist cancellation source is invalid.");
        var utf8 = new UTF8Encoding(false, true);
        _ = utf8.GetByteCount(cancellation.SourceId);
        if (cancellation.ChildInvocationId is { } child) { ArgumentException.ThrowIfNullOrWhiteSpace(child); _ = utf8.GetByteCount(child); }
    }

    internal static void Match(SpecialistOperationDocument operation, SpecialistInvocationIdentity identity)
    {
        Validate(operation);
        if (!SpecialistTurnDeliveryRules.SameLogical(operation.Identity, identity)) throw Conflict();
    }

    internal static bool Cancelled(SpecialistOperationDocument operation, SpecialistInvocationIdentity identity) =>
        operation.Cancellations.Any(receipt => receipt.Cancellation.Identity == identity);

    internal static (SpecialistOperationDocument Operation, SpecialistCancellationReceipt Receipt, SpecialistTurnDocument? Turn) Cancel(
        SpecialistOperationDocument operation, SpecialistTurnCancellation cancellation, SpecialistTurnDocument? turn)
    {
        Cancellation(cancellation);
        Match(operation, cancellation.Identity);
        if (operation.Cancellations.SingleOrDefault(item => item.Cancellation.Identity == cancellation.Identity) is { } prior)
        {
            // Equivalent native responsibility can be committed more than once. Keep its first
            // physical source; another timestamp/content/fingerprint never overrides the decision.
            if (prior.Cancellation with { SourceId = cancellation.SourceId } != cancellation) throw Conflict();
            return (operation, prior, turn);
        }
        if (operation.Cancellations.Count >= 64)
            throw new SpecialistJournalException("specialist.journal.capacity", turn?.Handle, "This operation reached its bounded cancellation capacity.", new InvalidOperationException("At most64 native parent cancellations are retained."));
        var outcome = SpecialistCancellationOutcome.CancelledBeforeAdmission;
        int? generation = null;
        var next = turn;
        if (turn is not null)
        {
            if (!SpecialistTurnDeliveryRules.SameLogical(turn.Identity, cancellation.Identity)) throw Conflict();
            var boundChild = turn.DeliveryProtocol == 0 ? turn.Identity == cancellation.Identity ? turn.ChildInvocationId : null :
                turn.Deliveries!.SingleOrDefault(item => item.Identity == cancellation.Identity)?.ChildInvocationId;
            if (boundChild is not null && cancellation.ChildInvocationId is not null && boundChild != cancellation.ChildInvocationId) throw Conflict();
            var ownsPhysical = turn.DeliveryProtocol == 0 ? turn.Identity == cancellation.Identity : turn.ActiveDeliveryActivityId == cancellation.Identity.ActivityExecutionId;
            var ownsPrepared = turn.Status == SpecialistTurnStatus.Prepared && turn.Identity == cancellation.Identity &&
                (turn.DeliveryProtocol == 0 || !turn.Deliveries!.Any(item => item.Identity != cancellation.Identity && !Cancelled(operation, item.Identity)));
            if (ownsPhysical || ownsPrepared) generation = turn.AdmissionGeneration;
            outcome = turn.Status switch
            {
                SpecialistTurnStatus.Accepted => SpecialistCancellationOutcome.AlreadyAccepted,
                SpecialistTurnStatus.Failed or SpecialistTurnStatus.Cancelled => SpecialistCancellationOutcome.AlreadyTerminal,
                _ when ownsPhysical || ownsPrepared => SpecialistCancellationOutcome.Cancelled,
                _ => SpecialistCancellationOutcome.OtherDeliveryUnchanged
            };
            if (outcome == SpecialistCancellationOutcome.Cancelled)
                next = turn with { Status = SpecialistTurnStatus.Cancelled, FailureCode = "specialist.turn.cancelled" };
        }
        var receipt = new SpecialistCancellationReceipt(cancellation, outcome, generation);
        var updated = operation with { Cancellations = [.. operation.Cancellations, receipt] };
        Validate(updated);
        if (next is not null) SpecialistTurnDocumentRules.Validate(next);
        return (updated, receipt, next);
    }

    internal static SpecialistJournalException Conflict() => new("specialist.journal.conflict", null,
        "The cancellation does not match the retained native operation or source.", new InvalidOperationException("The native cancellation fingerprint changed."));
}
