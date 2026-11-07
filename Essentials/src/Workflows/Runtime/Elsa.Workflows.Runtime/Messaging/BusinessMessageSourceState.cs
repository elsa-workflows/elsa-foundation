using Elsa.Workflows.Runtime.Core.Messaging;

namespace Elsa.Workflows.Runtime.Messaging;

internal enum BusinessMessageDefinitionStatus { Pending = 0, Selected = 1, Delivered = 2, Expired = 3, Unmatched = 4, Failed = 5, Abandoned = 6 }
internal enum BusinessMessageEvidenceKind { Consumption = 0, ClosedUnconsumed = 1, AuditedAbandon = 2 }
internal enum BusinessMessageFailure { PayloadIncompatible = 0, PermanentRecipientRefusal = 1 }

// References are DATA, not target capture, reservation, consumption or release authority. A future typed provider
// must resolve the actual retained owner evidence under the real target/source fence before any such transition.
internal sealed record BusinessMessageSelectionReference(string Id, long CapabilityEpoch, DateTimeOffset SelectedAt)
{ public override string ToString() => nameof(BusinessMessageSelectionReference); }
internal sealed record BusinessMessageEvidenceReference(BusinessMessageEvidenceKind Kind, string Id,
    string? SelectionId, long? CapabilityEpoch)
{ public override string ToString() => nameof(BusinessMessageEvidenceReference); }
internal sealed record BusinessMessageDiscoveryReference(long Generation, DateTimeOffset NotBefore);

internal sealed class BusinessMessageDefinitionProgress
{
    // Canonical decode data only. There is deliberately no Selected/Delivered/Failed transition factory.
    internal BusinessMessageDefinitionProgress(MessageDefinitionIdentity definition, BusinessMessageDefinitionStatus status,
        BusinessMessageDiscoveryReference? discovery = null, BusinessMessageSelectionReference? selection = null,
        DateTimeOffset? resolvedAt = null, BusinessMessageEvidenceReference? evidence = null, BusinessMessageFailure? failure = null)
    { ArgumentNullException.ThrowIfNull(definition); Definition = definition; Status = status; Discovery = discovery; Selection = selection; ResolvedAt = resolvedAt; Evidence = evidence; Failure = failure; }
    internal MessageDefinitionIdentity Definition { get; }
    internal BusinessMessageDefinitionStatus Status { get; }
    internal BusinessMessageDiscoveryReference? Discovery { get; }
    internal BusinessMessageSelectionReference? Selection { get; }
    internal DateTimeOffset? ResolvedAt { get; }
    internal BusinessMessageEvidenceReference? Evidence { get; }
    internal BusinessMessageFailure? Failure { get; }
    internal bool IsTerminal => Status is not (BusinessMessageDefinitionStatus.Pending or BusinessMessageDefinitionStatus.Selected);
    public override string ToString() => nameof(BusinessMessageDefinitionProgress);
}

/// <summary>Read-only canonical source data. Neither construction nor decoding proves acceptance or settlement.</summary>
internal sealed class BusinessMessageSourceState
{
    internal const int Version = 1;
    internal const long ImmutableHeaderRevision = 1;
    internal const int Open = 0;
    internal const int Terminal = 1;

    internal BusinessMessageSourceState(BusinessMessageAcceptedHeader header, BusinessMessagePayloadSlot payloadSlot,
        IReadOnlyCollection<BusinessMessageDefinitionProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(payloadSlot); ArgumentNullException.ThrowIfNull(progress);
        var values = progress.ToArray();
        if (values.Length != header.Definitions.Count) throw Invalid();
        if (values.Any(item => item is null) || values.Select(item => item.Definition).Distinct().Count() != values.Length) throw Invalid();
        var ordered = values.OrderBy(item => item.Definition.DefinitionNamespace, StringComparer.Ordinal)
            .ThenBy(item => item.Definition.DefinitionId, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Definition != header.Definitions[index]) throw Invalid();
            Validate(header, ordered[index]);
        }
        Header = header; PayloadSlot = payloadSlot; Progress = Array.AsReadOnly(ordered);
        if (payloadSlot.State == BusinessMessagePayloadState.Present) _ = payloadSlot.BindForPlanning(header);
        else if (ordered.Any(item => !item.IsTerminal)) throw Invalid();
    }

    internal static BusinessMessageSourceState InitialBuffered(BusinessMessageAcceptedHeader header, BusinessMessagePayloadSlot payloadSlot)
    {
        ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(payloadSlot);
        if (header.Receipt.Mode != MessageDeliveryMode.Buffered) throw new NotSupportedException("Immediate-only requires completed admission-time decisions.");
        if (payloadSlot.State != BusinessMessagePayloadState.Present) throw Invalid();
        return new(header, payloadSlot, header.Definitions.Select(definition => new BusinessMessageDefinitionProgress(definition,
            BusinessMessageDefinitionStatus.Pending, new(1, header.Receipt.AcceptedAt))).ToArray());
    }

    internal static BusinessMessageSourceState InitialImmediateUnmatched(BusinessMessageAcceptedHeader header,
        BusinessMessagePayloadSlot payloadSlot, IReadOnlyCollection<MessageDefinitionIdentity> completeUnmatchedCohort)
    {
        ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(payloadSlot); ArgumentNullException.ThrowIfNull(completeUnmatchedCohort);
        if (header.Receipt.Mode != MessageDeliveryMode.ImmediateOnly) throw new NotSupportedException("Only immediate-only admission can record Unmatched.");
        if (payloadSlot.State != BusinessMessagePayloadState.Present) throw Invalid();
        return new(header, payloadSlot, completeUnmatchedCohort.Select(definition => new BusinessMessageDefinitionProgress(definition,
            BusinessMessageDefinitionStatus.Unmatched, resolvedAt: header.Receipt.AcceptedAt)).ToArray());
    }

    internal BusinessMessageAcceptedHeader Header { get; }
    internal BusinessMessagePayloadSlot PayloadSlot { get; }
    internal IReadOnlyList<BusinessMessageDefinitionProgress> Progress { get; }
    internal bool HasOwedWork => Progress.Any(item => !item.IsTerminal);
    // Physical work eligibility only. Empty progress is not proof that any business recipient consumed.
    internal int Status => HasOwedWork ? Open : Terminal;
    // Empty immediate cohorts remain explicit unmatched, never vacuously Delivered.
    internal bool IsImmediateUnmatched => Header.Receipt.Mode == MessageDeliveryMode.ImmediateOnly &&
        Progress.All(item => item.Status == BusinessMessageDefinitionStatus.Unmatched);
    internal BusinessMessageDefinitionProgress Definition(MessageDefinitionIdentity definition) =>
        Progress.SingleOrDefault(item => item.Definition == definition) ?? throw Invalid();

    // Read-only monotonicity checks, NOT transition authorization. Actual selection/settlement/abandonment is
    // deliberately refused by this slice even if the caller supplies plausible proof-reference strings.
    internal static void ValidateSuccessorShape(BusinessMessageSourceState prior, BusinessMessageSourceState next)
    {
        ArgumentNullException.ThrowIfNull(prior); ArgumentNullException.ThrowIfNull(next);
        if (BusinessMessageAcceptedHeaderCodec.Fingerprint(prior.Header) != BusinessMessageAcceptedHeaderCodec.Fingerprint(next.Header)) throw Invalid();
        if (prior.PayloadSlot.State == BusinessMessagePayloadState.Retired &&
            (next.PayloadSlot.State != BusinessMessagePayloadState.Retired || prior.PayloadSlot.RetirementOperationId != next.PayloadSlot.RetirementOperationId ||
             prior.PayloadSlot.RetirementOperationFingerprint != next.PayloadSlot.RetirementOperationFingerprint)) throw Invalid();
        if (prior.PayloadSlot.State != next.PayloadSlot.State)
            throw new NotSupportedException("Payload retirement requires actual retained-reference and cleanup evidence.");
        for (var index = 0; index < prior.Progress.Count; index++)
        {
            var before = prior.Progress[index]; var after = next.Progress[index];
            if (before.Selection is not null && before.Selection != after.Selection) throw Invalid();
            if (before.IsTerminal && BusinessMessageSourceCodec.ProgressFingerprint(before) != BusinessMessageSourceCodec.ProgressFingerprint(after)) throw Invalid();
            if (before.Status == BusinessMessageDefinitionStatus.Pending && after.Status == BusinessMessageDefinitionStatus.Pending)
            {
                var old = before.Discovery!; var current = after.Discovery!;
                if (current.Generation == old.Generation ? current.NotBefore != old.NotBefore :
                    current.Generation != checked(old.Generation + 1) || current.NotBefore < old.NotBefore) throw Invalid();
            }
            if (before.Status != after.Status &&
                (before.Selection is not null || after.Selection is not null || after.Status == BusinessMessageDefinitionStatus.Abandoned))
                throw new NotSupportedException("A typed provider must validate actual selection, target or audited closure evidence.");
        }
    }

    private static void Validate(BusinessMessageAcceptedHeader header, BusinessMessageDefinitionProgress item)
    {
        if (!Enum.IsDefined(item.Status) || item.Failure is { } failure && !Enum.IsDefined(failure)) throw Invalid();
        if (item.Selection is { } selected)
        {
            Text(selected.Id); if (selected.CapabilityEpoch <= 0 || selected.SelectedAt < header.Receipt.AcceptedAt) throw Invalid();
            if (header.Receipt.Mode == MessageDeliveryMode.ImmediateOnly ? selected.SelectedAt != header.Receipt.AcceptedAt :
                selected.SelectedAt >= header.Receipt.BufferUntil) throw Invalid();
        }
        if (item.Evidence is { } evidence)
        {
            Text(evidence.Id); if (!Enum.IsDefined(evidence.Kind)) throw Invalid();
            if (item.Selection is { } winner)
            { if (evidence.SelectionId != winner.Id || evidence.CapabilityEpoch != winner.CapabilityEpoch) throw Invalid(); }
            else if (evidence.SelectionId is not null || evidence.CapabilityEpoch is not null) throw Invalid();
        }
        if (item.IsTerminal)
        {
            if (item.ResolvedAt is null || item.ResolvedAt < (item.Selection?.SelectedAt ?? header.Receipt.AcceptedAt)) throw Invalid();
        }
        else if (item.ResolvedAt is not null) throw Invalid();
        switch (item.Status)
        {
            case BusinessMessageDefinitionStatus.Pending:
                if (header.Receipt.Mode != MessageDeliveryMode.Buffered || item.Discovery is not { Generation: > 0 } discovery ||
                    discovery.NotBefore < header.Receipt.AcceptedAt || item.Selection is not null || item.Evidence is not null || item.Failure is not null) throw Invalid();
                break;
            case BusinessMessageDefinitionStatus.Selected:
                if (item.Selection is null || item.Discovery is not null || item.Evidence is not null || item.Failure is not null) throw Invalid();
                break;
            case BusinessMessageDefinitionStatus.Delivered:
                if (item.Selection is null || item.Discovery is not null || item.Evidence?.Kind != BusinessMessageEvidenceKind.Consumption || item.Failure is not null) throw Invalid();
                break;
            case BusinessMessageDefinitionStatus.Failed:
                if (item.Selection is null || item.Discovery is not null || item.Evidence?.Kind != BusinessMessageEvidenceKind.ClosedUnconsumed || item.Failure is null) throw Invalid();
                break;
            case BusinessMessageDefinitionStatus.Expired:
                if (header.Receipt.Mode != MessageDeliveryMode.Buffered || item.ResolvedAt < header.Receipt.BufferUntil ||
                    item.Selection is not null || item.Discovery is not null || item.Evidence is not null || item.Failure is not null) throw Invalid();
                break;
            case BusinessMessageDefinitionStatus.Unmatched:
                if (header.Receipt.Mode != MessageDeliveryMode.ImmediateOnly || item.ResolvedAt != header.Receipt.AcceptedAt ||
                    item.Selection is not null || item.Discovery is not null || item.Evidence is not null || item.Failure is not null) throw Invalid();
                break;
            case BusinessMessageDefinitionStatus.Abandoned:
                if (item.Discovery is not null || item.Failure is not null || (item.Selection is null
                    ? header.Receipt.Mode != MessageDeliveryMode.Buffered || item.Evidence?.Kind != BusinessMessageEvidenceKind.AuditedAbandon
                    : item.Evidence?.Kind != BusinessMessageEvidenceKind.ClosedUnconsumed)) throw Invalid();
                break;
        }
    }
    private static void Text(string value) { if (string.IsNullOrWhiteSpace(value)) throw Invalid(); }
    internal static InvalidDataException Invalid() => new("The canonical BusinessMessage progress or work is invalid.");
    public override string ToString() => nameof(BusinessMessageSourceState);
}
