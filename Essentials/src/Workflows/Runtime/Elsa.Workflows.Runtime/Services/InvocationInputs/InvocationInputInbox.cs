using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.InvocationInputs;

// Pure internal proposals only. Native actor/checkpoint, authorization, value ownership, schema and
// retention participants must be integrated before any result can become an admission acknowledgment.
internal sealed record InvocationInputPolicy
{
    internal InvocationInputPolicy(int pendingCapacity, int receiptCapacity, TimeSpan retention,
        int maximumIdLength, int maximumContentBytes, int maximumDepth)
    {
        if (pendingCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pendingCapacity));
        if (receiptCapacity < pendingCapacity) throw new ArgumentOutOfRangeException(nameof(receiptCapacity));
        if (retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
        if (maximumIdLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumIdLength));
        if (maximumContentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumContentBytes));
        if (maximumDepth is <= 0 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        PendingCapacity = pendingCapacity; ReceiptCapacity = receiptCapacity; Retention = retention;
        MaximumIdLength = maximumIdLength; MaximumContentBytes = maximumContentBytes; MaximumDepth = maximumDepth;
    }

    internal int PendingCapacity { get; }
    internal int ReceiptCapacity { get; }
    internal TimeSpan Retention { get; }
    internal int MaximumIdLength { get; }
    internal int MaximumContentBytes { get; }
    internal int MaximumDepth { get; }
}

internal enum InvocationInputStatus { Queued, Evaluating, Selected, Handled, Unhandled, NotProcessed, Interrupted }
internal enum InvocationAdmissionKind { Proposed, Existing, Conflict, PendingCapacity, ReceiptCapacity, OrdinalExhausted, Ended }
internal sealed record InvocationInputReceipt(string InputId, int FingerprintVersion, string Fingerprint,
    long Ordinal, DateTimeOffset ProposedAt, InvocationInputPolicy Policy, JsonElement? Content,
    InvocationInputStatus Status = InvocationInputStatus.Queued, string? EvaluationId = null,
    string? SelectionId = null, string? ProgressEvidence = null, string? FinalEvidence = null,
    DateTimeOffset? FinalAt = null, DateTimeOffset? RetainUntil = null)
{
    internal bool IsFinal => Status is InvocationInputStatus.Handled or InvocationInputStatus.Unhandled or
        InvocationInputStatus.NotProcessed or InvocationInputStatus.Interrupted;
}
internal sealed record InvocationAdmissionProposal(InvocationAdmissionKind Kind, InvocationInputInbox Inbox,
    InvocationInputReceipt? Receipt = null);

/// <summary>Immutable bounded invocation input accounting, with no effects, clock or persistence authority.</summary>
internal sealed class InvocationInputInbox
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly IReadOnlyDictionary<string, InvocationInputReceipt> receipts;

    internal InvocationInputInbox(InvocationInputPolicy policy, long lastOrdinal = 0)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (lastOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(lastOrdinal));
        Policy = policy; LastOrdinal = lastOrdinal;
        receipts = Snapshot(new Dictionary<string, InvocationInputReceipt>(StringComparer.Ordinal));
    }

    private InvocationInputInbox(InvocationInputInbox previous, Dictionary<string, InvocationInputReceipt>? next = null,
        long? lastOrdinal = null, string? ended = null, InvocationInputPolicy? policy = null)
    {
        Policy = policy ?? previous.Policy; LastOrdinal = lastOrdinal ?? previous.LastOrdinal;
        EndedReason = ended ?? previous.EndedReason; Revision = checked(previous.Revision + 1);
        receipts = next is null ? previous.receipts : Snapshot(next);
    }

    internal InvocationInputPolicy Policy { get; }
    internal long LastOrdinal { get; }
    internal long Revision { get; }
    internal string? EndedReason { get; }
    internal int PendingCount => receipts.Values.Count(receipt => !receipt.IsFinal);
    internal int ReceiptCount => receipts.Count;
    internal IReadOnlyList<InvocationInputReceipt> Receipts => Array.AsReadOnly(receipts.Values.OrderBy(receipt => receipt.Ordinal).ToArray());
    internal InvocationInputReceipt? Find(string inputId) => receipts.GetValueOrDefault(inputId);

    internal InvocationAdmissionProposal ProposeAdmission(string inputId, JsonElement content, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId); RequireTime(observedAt);
        // A later policy must not invalidate the existing identity or shorten its original promise.
        var existing = Find(inputId);
        var effective = existing?.Policy ?? Policy;
        Validate(inputId, content, effective);
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeInvocationInput(existing?.FingerprintVersion ?? 1, content);
        if (existing is not null)
            return new(existing.Fingerprint == fingerprint ? InvocationAdmissionKind.Existing : InvocationAdmissionKind.Conflict, this, existing);
        if (EndedReason is not null) return new(InvocationAdmissionKind.Ended, this);
        if (PendingCount >= Policy.PendingCapacity) return new(InvocationAdmissionKind.PendingCapacity, this);
        if (ReceiptCount >= Policy.ReceiptCapacity) return new(InvocationAdmissionKind.ReceiptCapacity, this);
        if (LastOrdinal == long.MaxValue) return new(InvocationAdmissionKind.OrdinalExhausted, this);
        _ = observedAt.Add(Policy.Retention); // Refuse an already unrepresentable promise before proposing a reservation.
        var ordinal = checked(LastOrdinal + 1);
        var receipt = new InvocationInputReceipt(inputId, 1, fingerprint, ordinal, observedAt, Policy, content.Clone());
        var next = Copy(); next.Add(inputId, receipt);
        return new(InvocationAdmissionKind.Proposed, new(this, next, ordinal), receipt);
    }

    internal InvocationInputInbox BeginEvaluation(string inputId, string evaluationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluationId);
        var receipt = Require(inputId);
        if (receipt.Status == InvocationInputStatus.Evaluating && receipt.EvaluationId == evaluationId) return this;
        if (receipt.Status != InvocationInputStatus.Queued || EndedReason is not null ||
            receipts.Values.Any(item => item.Status is InvocationInputStatus.Evaluating or InvocationInputStatus.Selected) ||
            receipts.Values.Any(item => item.Status == InvocationInputStatus.Queued && item.Ordinal < receipt.Ordinal))
            throw new InvalidOperationException("Only the first queued input can begin an unblocked evaluation.");
        return Replace(receipt with { Status = InvocationInputStatus.Evaluating, EvaluationId = evaluationId });
    }

    internal InvocationInputInbox Select(string inputId, string selectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionId);
        var receipt = Require(inputId);
        if (receipt.Status == InvocationInputStatus.Selected && receipt.SelectionId == selectionId) return this;
        if (receipt.Status != InvocationInputStatus.Evaluating)
            throw new InvalidOperationException("Selection requires the recorded input evaluation.");
        return Replace(receipt with { Status = InvocationInputStatus.Selected, SelectionId = selectionId });
    }

    internal InvocationInputInbox RecordProgress(string inputId, string evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        var receipt = Require(inputId);
        if (receipt.Status != InvocationInputStatus.Selected)
            throw new InvalidOperationException("Only a selected input can record phase progress.");
        return receipt.ProgressEvidence == evidence ? this : Replace(receipt with { ProgressEvidence = evidence });
    }

    internal InvocationInputInbox Finish(string inputId, InvocationInputStatus status, string evidence, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence); RequireTime(observedAt);
        var receipt = Require(inputId);
        if (receipt.IsFinal)
            return receipt.Status == status && receipt.FinalEvidence == evidence ? this :
                throw new InvalidOperationException("A final input disposition cannot change.");
        if (!(status == InvocationInputStatus.Unhandled && receipt.Status == InvocationInputStatus.Evaluating ||
              status == InvocationInputStatus.Handled && receipt.Status == InvocationInputStatus.Selected))
            throw new InvalidOperationException("Final disposition does not match the recorded evaluation/selection boundary.");
        return Replace(Settle(receipt, status, evidence, observedAt));
    }

    internal InvocationInputInbox End(string reason, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason); RequireTime(observedAt);
        if (EndedReason is not null)
            return EndedReason == reason ? this : throw new InvalidOperationException("The invocation's terminal reason cannot change.");
        var next = Copy();
        foreach (var receipt in receipts.Values.Where(item => !item.IsFinal))
            next[receipt.InputId] = Settle(receipt, receipt.Status == InvocationInputStatus.Selected
                ? InvocationInputStatus.Interrupted : InvocationInputStatus.NotProcessed, reason, observedAt);
        return new(this, next, ended: reason);
    }

    // The runtime retention owner must independently establish absence of source/transport/value obligations.
    // These IDs are proposals for that owner, not deletion authority supplied by an API caller or by this reducer.
    internal InvocationInputInbox ReleaseReceipts(IReadOnlyCollection<string> obligationFreeIds, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(obligationFreeIds); RequireTime(observedAt);
        if (obligationFreeIds.Count == 0) return this;
        if (obligationFreeIds.Distinct(StringComparer.Ordinal).Count() != obligationFreeIds.Count)
            throw new ArgumentException("Receipt release cannot repeat an identity.", nameof(obligationFreeIds));
        foreach (var id in obligationFreeIds)
        {
            var receipt = Require(id);
            if (!receipt.IsFinal || receipt.RetainUntil is null || observedAt < receipt.RetainUntil)
                throw new InvalidOperationException("An unsettled or promised receipt cannot be released.");
        }
        var next = Copy(); foreach (var id in obligationFreeIds) next.Remove(id);
        return new(this, next);
    }

    internal InvocationInputInbox WithPolicy(InvocationInputPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy == Policy ? this : new(this, policy: policy);
    }

    private InvocationInputReceipt Require(string inputId) => Find(inputId)
        ?? throw new InvalidOperationException("The invocation does not own that input identity.");
    private InvocationInputInbox Replace(InvocationInputReceipt receipt)
    {
        var next = Copy(); next[receipt.InputId] = receipt; return new(this, next);
    }
    private Dictionary<string, InvocationInputReceipt> Copy() => new(receipts, StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, InvocationInputReceipt> Snapshot(Dictionary<string, InvocationInputReceipt> source) =>
        new ReadOnlyDictionary<string, InvocationInputReceipt>(source);
    private static InvocationInputReceipt Settle(InvocationInputReceipt receipt, InvocationInputStatus status,
        string evidence, DateTimeOffset now)
    {
        if (now < receipt.ProposedAt) throw new InvalidOperationException("A disposition cannot precede its input.");
        return receipt with { Status = status, Content = null, FinalEvidence = evidence,
            FinalAt = now, RetainUntil = now.Add(receipt.Policy.Retention) };
    }
    private static void RequireTime(DateTimeOffset time)
    {
        if (time == default || time == DateTimeOffset.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(time), "A finite trusted observation time is required.");
    }
    private static void Validate(string id, JsonElement content, InvocationInputPolicy policy)
    {
        if (id.Length > policy.MaximumIdLength) throw new ArgumentException("Input identity exceeds its declared limit.", nameof(id));
        try { _ = StrictUtf8.GetByteCount(id); }
        catch (EncoderFallbackException exception) { throw new ArgumentException("Input identity requires well-formed Unicode.", nameof(id), exception); }
        if (content.ValueKind != JsonValueKind.Object || StrictUtf8.GetByteCount(content.GetRawText()) > policy.MaximumContentBytes)
            throw new ArgumentException("Input semantics exceed their declared object/size contract.", nameof(content));
        CheckDepth(content, 0);
        void CheckDepth(JsonElement value, int depth)
        {
            var nestedDepth = depth + (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? 1 : 0);
            if (nestedDepth > policy.MaximumDepth) throw new ArgumentException("Input semantics exceed their declared depth.", nameof(content));
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var property in value.EnumerateObject()) CheckDepth(property.Value, nestedDepth);
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) CheckDepth(item, nestedDepth);
        }
    }
}
