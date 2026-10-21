using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>Common persisted state checks, independent of an installed rule language implementation.</summary>
internal static class RuleRowCodec
{
    internal sealed record State(RuleInvocationEnvelope Invocation, RuleFact[] Facts, RuleTerminalResult? Terminal);

    internal static RuleRunRecord Create(RuleInvocationEnvelope invocation, string key, string identity, string json, string fingerprint)
    {
        if (invocation.NativeDestination is { ResultSchemaVersion: not 1 })
            throw new NotSupportedException("rules.result-schema");
        return new()
        {
            RunKey = key, IdentityBase64 = identity, InvocationJson = json, Fingerprint64 = fingerprint,
            PinHash64 = RuleRecordCodec.PinHash(invocation.ExpectedImplementation), ModelRevision = 1,
            FactsJson = "[]", FactsSha25664 = RuleRecordCodec.FactsHash([]),
            Status = (int)RuleRunStatus.Running, SchemaVersion = RulesEfModule.SchemaVersion
        };
    }

    internal static void Settle(RuleRunRecord row, RuleInvocationEnvelope invocation, IReadOnlyList<RuleFact> facts,
        RuleTerminalKind kind, RuleRunDiagnostic? diagnostic)
    {
        // Fixed ASCII fields with fixed-width fingerprint; version and separators make the identity unambiguous.
        var terminalId = TerminalId(row, kind);
        var result = new RuleTerminalResult(terminalId, kind, row.CommittedRounds,
            invocation.NativeDestination?.ResultSchemaVersion ?? 1, facts, row.FactsSha25664, diagnostic);
        row.TerminalJson = Encoding.UTF8.GetString(RuleRecordCodec.TerminalBytes(result));
        row.TerminalId64 = terminalId;
        row.TerminalModelRevision = row.ModelRevision;
        row.Status = kind switch
        {
            RuleTerminalKind.Succeeded => (int)RuleRunStatus.Succeeded,
            RuleTerminalKind.Faulted => (int)RuleRunStatus.Faulted,
            RuleTerminalKind.Cancelled => (int)RuleRunStatus.Cancelled,
            RuleTerminalKind.Refused => (int)RuleRunStatus.Refused,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        // A pending receipt stays in immutable history; its additions never enter a cancellation result.
        row.PendingReceiptId64 = null;
        row.WorkOwner = null;
        row.WorkExpiresAtUtcTicks = null;
        if (invocation.NativeDestination is { } destination)
        {
            var deliveryId = DeliveryId(terminalId);
            row.DeliveryId64 = deliveryId;
            row.DeliveryPayloadJson = Encoding.UTF8.GetString(RuleRecordCodec.DeliveryRecordBytes(deliveryId, destination, result));
            row.DeliveryPayloadSha25664 = RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes(deliveryId, destination, result));
        }
    }

    internal static State Read(RuleRunRecord row)
    {
        try { return ReadCore(row); }
        // JSON scalar getters report InvalidOperationException for an incompatible persisted shape.
        catch (InvalidOperationException error) { throw new InvalidDataException("rules.persisted-shape", error); }
    }

    private static State ReadCore(RuleRunRecord row)
    {
        CheckProjectionSize(row);
        if (row.SchemaVersion != RulesEfModule.SchemaVersion) throw new NotSupportedException("rules.provider-schema");
        Require(Enum.IsDefined((RuleRunStatus)row.Status) && row.ModelRevision > 0
            && row.ConcurrencyToken is { Length: 16 } && row.CommittedRounds is >= 0 and <= RuleLimits.MaxRounds, "rules.persisted-state");
        var invocation = RuleRecordCodec.ReadInvocation(Bytes(row.InvocationJson));
        var scope = RuleRecordCodec.ScopeBytes(invocation.Identity);
        Require(row.RunKey == RuleRecordCodec.Hash(scope) && row.IdentityBase64 == Convert.ToBase64String(scope)
            && row.Fingerprint64 == RuleRecordCodec.Fingerprint(invocation).Sha256
            && row.PinHash64 == RuleRecordCodec.PinHash(invocation.ExpectedImplementation), "rules.persisted-identity");
        var facts = RuleRecordCodec.ReadFacts(Bytes(row.FactsJson)).ToArray();
        Require(row.FactsSha25664 == RuleRecordCodec.FactsHash(facts), "rules.persisted-facts");
        Require(row.Admitted || (facts.Length == 0 && row.CommittedRounds == 0 && row.PendingReceiptId64 is null), "rules.unadmitted-state");
        Require(row.CommittedRounds <= invocation.MaxRounds, "rules.persisted-round-count");
        var factNames = facts.Select(fact => fact.Atom).ToHashSet(StringComparer.Ordinal);
        Require(!row.Admitted || invocation.InitialFacts.All(fact => factNames.Contains(fact.Atom)), "rules.missing-initial-fact");
        Require(LeaseShape(row.WorkOwner, row.WorkFence, row.WorkExpiresAtUtcTicks)
            && LeaseShape(row.DeliveryOwner, row.DeliveryFence, row.DeliveryExpiresAtUtcTicks), "rules.persisted-lease");
        if ((RuleRunStatus)row.Status == RuleRunStatus.Running)
        {
            Require(row.CommittedRounds < invocation.MaxRounds, "rules.running-round-budget");
            Require(row.Admitted && row.TerminalId64 is null && row.TerminalJson is null && row.TerminalModelRevision is null
                && row.DeliveryId64 is null && row.DeliveryPayloadJson is null && row.DeliveryPayloadSha25664 is null
                && row.DeliveryOwner is null && row.DeliveryExpiresAtUtcTicks is null && row.DeliveryFence == 0
                && !row.DeliveryAcknowledged && row.DeliveryAcknowledgementJson is null,
                "rules.running-terminal");
            return new(invocation, facts, null);
        }
        Require(row.TerminalJson is not null && row.TerminalId64 is not null && row.TerminalModelRevision == row.ModelRevision
            && row.PendingReceiptId64 is null && row.WorkOwner is null && row.WorkExpiresAtUtcTicks is null, "rules.terminal-state");
        var terminal = RuleRecordCodec.ReadTerminal(Bytes(row.TerminalJson!));
        Require(terminal.TerminalId == row.TerminalId64 && terminal.TerminalId == TerminalId(row, terminal.Kind)
            && terminal.CommittedRounds == row.CommittedRounds
            && terminal.ResultSchemaVersion == (invocation.NativeDestination?.ResultSchemaVersion ?? 1)
            && terminal.FinalFactsSha256 == row.FactsSha25664
            && terminal.Kind.ToString() == ((RuleRunStatus)row.Status).ToString(), "rules.terminal-projection");
        Require(terminal.Kind != RuleTerminalKind.Refused || !row.Admitted, "rules.refused-admission");
        Require(row.Admitted || terminal.Kind is RuleTerminalKind.Refused or RuleTerminalKind.Cancelled, "rules.unadmitted-terminal");
        if (invocation.NativeDestination is { } destination)
        {
            Require(row.DeliveryId64 == DeliveryId(terminal.TerminalId) && row.DeliveryPayloadJson is not null
                && row.DeliveryAcknowledged == (row.DeliveryAcknowledgementJson is not null)
                && (!row.DeliveryAcknowledged || (row.DeliveryOwner is null && row.DeliveryExpiresAtUtcTicks is null)),
                "rules.terminal-delivery");
            // This slice cannot verify a durable parent-consumption proof yet. Never treat an opaque
            // acknowledgement string as settlement; delivery support must add its canonical proof reader.
            if (row.DeliveryAcknowledged) throw new NotSupportedException("rules.delivery-acknowledgement-schema");
            var envelope = RuleRecordCodec.ReadDeliveryRecord(Bytes(row.DeliveryPayloadJson!));
            Require(envelope.DeliveryId == row.DeliveryId64 && envelope.CanonicalPayloadSha256 == row.DeliveryPayloadSha25664
                && row.DeliveryPayloadJson == Encoding.UTF8.GetString(RuleRecordCodec.DeliveryRecordBytes(row.DeliveryId64!, destination, terminal)),
                "rules.delivery-projection");
        }
        else
            Require(row.DeliveryId64 is null && row.DeliveryPayloadJson is null && row.DeliveryPayloadSha25664 is null
                && row.DeliveryOwner is null && row.DeliveryExpiresAtUtcTicks is null && !row.DeliveryAcknowledged
                && row.DeliveryAcknowledgementJson is null && row.DeliveryFence == 0, "rules.direct-delivery");
        return new(invocation, facts, terminal);
    }

    /// <summary>Actual encoded projection bound. Callers persisting a pending receipt supply its complete record.</summary>
    internal static void CheckProjectionSize(RuleRunRecord row, RuleRoundReceiptRecord? pendingReceipt = null)
    {
        // First bound all raw text before serializing metadata or allocating UTF-8 copies from a provider read.
        var metadataStrings = new[] { row.RunKey, row.IdentityBase64, row.Fingerprint64, row.PinHash64, row.FactsSha25664,
            row.PendingReceiptId64, row.TerminalId64, row.WorkOwner, row.DeliveryId64, row.DeliveryPayloadSha25664,
            row.DeliveryOwner, row.DeliveryAcknowledgementJson, row.SchemaVersion,
            pendingReceipt?.RunKey, pendingReceipt?.ReceiptId64, pendingReceipt?.InputFactsSha25664,
            pendingReceipt?.AcceptedAttemptId, pendingReceipt?.AcceptedWorkerId, pendingReceipt?.ConsumptionTransitionId64,
            pendingReceipt?.TerminalIdAtConsumption64, pendingReceipt?.NextRoundIdAtConsumption64, pendingReceipt?.SchemaVersion };
        var rawMetadata = metadataStrings.Sum(value => value is null ? 0L : Encoding.UTF8.GetByteCount(value));
        Require(rawMetadata <= RuleLimits.MaxStateMetadataBytes, "rules.metadata-too-large");
        var receiptMetadata = pendingReceipt is null ? null : new
        {
            pendingReceipt.RunKey, pendingReceipt.Ordinal, pendingReceipt.ReceiptId64, pendingReceipt.InputFactsSha25664,
            pendingReceipt.AcceptedAttemptId, pendingReceipt.AcceptedWorkerId, pendingReceipt.AcceptedFence,
            pendingReceipt.AcceptedModelRevision, pendingReceipt.ConsumedModelRevision, pendingReceipt.ConsumptionTransitionId64,
            pendingReceipt.TerminalIdAtConsumption64, pendingReceipt.NextRoundIdAtConsumption64, pendingReceipt.SchemaVersion
        };
        // Account for the token that SaveChanges will stamp, including before the first save.
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            row.RunKey, row.IdentityBase64, row.Fingerprint64, row.PinHash64, row.Admitted, row.ModelRevision,
            ConcurrencyToken = new byte[16], row.FactsSha25664, row.CommittedRounds, row.PendingReceiptId64,
            row.Status, row.TerminalId64, row.TerminalModelRevision, row.WorkOwner, row.WorkFence,
            row.WorkExpiresAtUtcTicks, row.DeliveryId64, row.DeliveryPayloadSha25664, row.DeliveryOwner,
            row.DeliveryFence, row.DeliveryExpiresAtUtcTicks, row.DeliveryAcknowledged,
            row.DeliveryAcknowledgementJson, row.SchemaVersion, PendingReceipt = receiptMetadata
        });
        Require(metadata.Length <= RuleLimits.MaxStateMetadataBytes, "rules.metadata-too-large");
        long size = metadata.Length;
        foreach (var value in new[] { row.InvocationJson, row.FactsJson, pendingReceipt?.ReceiptJson, row.TerminalJson, row.DeliveryPayloadJson })
        {
            if (value is null) continue;
            var count = Encoding.UTF8.GetByteCount(value);
            Require(count <= RuleLimits.MaxEnvelopeBytes, "rules.record-too-large");
            size += count;
        }
        Require(size <= RuleLimits.MaxStateProjectionBytes, "rules.projection-too-large");
    }

    private static string TerminalId(RuleRunRecord row, RuleTerminalKind kind) =>
        RuleRecordCodec.Hash(Encoding.UTF8.GetBytes("rules.terminal:1:" + row.Fingerprint64 + ":"
            + row.ModelRevision.ToString(CultureInfo.InvariantCulture) + ":" + kind));

    private static string DeliveryId(string terminalId) =>
        RuleRecordCodec.Hash(Encoding.UTF8.GetBytes("rules.delivery:1:" + terminalId));

    private static bool LeaseShape(string? owner, long fence, long? expiresAtUtcTicks) =>
        fence >= 0 && (owner is null
            ? expiresAtUtcTicks is null
            : owner.Length > 0 && Encoding.UTF8.GetByteCount(owner) <= RuleLimits.MaxIdentifierUtf8Bytes
                && fence > 0 && expiresAtUtcTicks > 0 && expiresAtUtcTicks <= DateTime.MaxValue.Ticks);

    private static byte[] Bytes(string? value)
    {
        if (value is null) throw new InvalidDataException("rules.missing-record");
        return Encoding.UTF8.GetBytes(value);
    }

    private static void Require(bool condition, string diagnostic)
    {
        if (!condition) throw new InvalidDataException(diagnostic);
    }
}
