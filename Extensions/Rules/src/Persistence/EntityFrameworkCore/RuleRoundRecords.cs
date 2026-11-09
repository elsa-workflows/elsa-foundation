using System.Globalization;
using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>Common receipt integrity and deterministic transition identities, without interpreting the artifact language.</summary>
internal static class RuleRoundRecords
{
    internal static string RoundId(string fingerprint, int ordinal) => Digest("rules.round:1:" + fingerprint + ":" + ordinal.ToString(CultureInfo.InvariantCulture));
    internal static string ReceiptId(RuleRoundReceipt receipt) => Digest("rules.receipt:1:" + RoundId(receipt.Identity.Fingerprint.Sha256, receipt.Identity.Ordinal) + ":" + receipt.Identity.InputStateSha256);
    internal static string ConsumptionId(RuleStoredRoundReceipt receipt) => Digest("rules.consume:1:" + receipt.ReceiptId + ":" + receipt.Receipt.SemanticReceiptSha256);

    internal static RuleRoundInput Input(RuleRunRecord row, RuleRowCodec.State state)
    {
        Require(row.Status == (int)RuleRunStatus.Running && row.CommittedRounds < state.Invocation.MaxRounds, "rules.no-pending-round");
        return new(new(state.Invocation.Identity, new(row.Fingerprint64), row.CommittedRounds, row.FactsSha25664),
            state.Invocation.Artifact, state.Invocation.ExpectedImplementation, state.Facts, state.Invocation.MaxRounds, row.ModelRevision);
    }

    internal static RuleStoredRoundReceipt Read(RuleRunRecord run, RuleRoundReceiptRecord row)
    {
        if (row.SchemaVersion != RulesEfModule.SchemaVersion) throw new NotSupportedException("rules.receipt-provider-schema");
        Require(Encoding.UTF8.GetByteCount(row.ReceiptJson) <= RuleLimits.MaxEnvelopeBytes, "rules.receipt-too-large");
        var receipt = RuleRecordCodec.ReadReceiptRecord(Encoding.UTF8.GetBytes(row.ReceiptJson));
        Require(row.RunKey == run.RunKey && Convert.ToBase64String(RuleRecordCodec.ScopeBytes(receipt.Identity.Run)) == run.IdentityBase64
            && receipt.Identity.Fingerprint.Sha256 == run.Fingerprint64 && receipt.ImplementationPinHash == run.PinHash64
            && row.Ordinal == receipt.Identity.Ordinal && row.InputFactsSha25664 == receipt.Identity.InputStateSha256
            && row.ReceiptId64 == ReceiptId(receipt), "rules.receipt-projection");
        var attempt = new RuleRoundAttempt(row.AcceptedAttemptId, row.AcceptedWorkerId, row.AcceptedFence);
        Require(row.AcceptedModelRevision > 1 && row.AcceptedModelRevision <= run.ModelRevision, "rules.receipt-revision");
        Require(receipt.AddedFacts.Select(fact => fact.Atom).SequenceEqual(
            RuleRecordCodec.CanonicalFacts(receipt.AddedFacts).Select(fact => fact.Atom), StringComparer.Ordinal), "rules.noncanonical-additions");
        Require(receipt.FiringEvidence.Select(firing => firing.RuleId).SequenceEqual(
            receipt.FiringEvidence.Select(firing => firing.RuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "rules.noncanonical-firings");
        var stored = new RuleStoredRoundReceipt(row.ReceiptId64, receipt, attempt, row.AcceptedModelRevision, row.ConsumedModelRevision);
        if (row.ConsumedModelRevision is { } consumed)
        {
            Require(consumed > row.AcceptedModelRevision && consumed <= run.ModelRevision && row.Ordinal < run.CommittedRounds
                && row.ConsumptionTransitionId64 == ConsumptionId(stored), "rules.consumption-projection");
            Require(row.TerminalIdAtConsumption64 is { } terminal
                ? terminal == run.TerminalId64 && run.TerminalModelRevision == consumed && row.NextRoundIdAtConsumption64 is null
                : row.NextRoundIdAtConsumption64 == RoundId(run.Fingerprint64, row.Ordinal + 1), "rules.consumption-outcome");
        }
        else
            Require(row.ConsumptionTransitionId64 is null && row.TerminalIdAtConsumption64 is null && row.NextRoundIdAtConsumption64 is null,
                "rules.unconsumed-projection");
        return stored;
    }

    internal static void RequirePending(RuleRunRecord run, RuleStoredRoundReceipt receipt)
    {
        Require(run.PendingReceiptId64 == receipt.ReceiptId && receipt.ConsumedModelRevision is null
            && receipt.AcceptedModelRevision == run.ModelRevision && receipt.Receipt.Identity.Ordinal == run.CommittedRounds
            && receipt.Receipt.Identity.InputStateSha256 == run.FactsSha25664, "rules.pending-receipt-projection");
    }

    private static string Digest(string value) => RuleRecordCodec.Hash(Encoding.UTF8.GetBytes(value));
    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
