using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

/// <summary>Pure finite-language capacity proof. Package availability and persistence are separate gates.</summary>
internal static class RuleAdmissionCapacity
{
    // Fixed rows retain a bounded number of identity, lease, revision and confirmation fields.
    // This reserve is additional to every variable representation counted below; the provider
    // must enforce it on its actual metadata projection before committing a new row.
    public static RuleCapacityFootprint Measure(RuleInvocationEnvelope invocation)
    {
        var common = RuleInvocationCodec.InvocationBytes(invocation);
        if (invocation.NativeDestination is { ResultSchemaVersion: not 1 })
            throw new NotSupportedException("rules.destination-result-schema");
        var program = RulesCodec.ReadArtifact(invocation.Artifact);
        RulesCodec.ResolveMaxRounds(program, invocation.InitialFacts, invocation.MaxRounds);
        var vocabulary = RulesCodec.CanonicalFacts(program.Vocabulary);
        var fingerprint = new RuleInvocationFingerprint(RulesCodec.Hash(common));
        var round = new RuleRoundIdentity(invocation.Identity, fingerprint, invocation.MaxRounds - 1, RulesCodec.FactsHash(vocabulary));

        // All vocabulary facts plus every firing is a conservative superset of every reachable
        // receipt. This deliberately includes unreachable atoms and already-satisfied conclusions.
        var maximumReceipt = new RuleRoundReceipt(round, RulesCodec.PinHash(invocation.ExpectedImplementation), vocabulary,
            program.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal)
                .Select(rule => new RuleFiringEvidence(rule.Id, rule.Conclusion)).ToArray(), new string('f', 64));
        var receiptBytes = RulesCodec.ReceiptRecordBytes(maximumReceipt).Length;

        // A 256-byte control-character identifier expands most under the canonical JSON encoder.
        // Using it for both generated identities and diagnostic covers all contract-valid values.
        var longestEncodedIdentifier = new string('\u0001', RuleLimits.MaxIdentifierUtf8Bytes);
        var result = new RuleTerminalResult(longestEncodedIdentifier, RuleTerminalKind.Succeeded, invocation.MaxRounds,
            1, vocabulary, RulesCodec.FactsHash(vocabulary), new(longestEncodedIdentifier));
        var terminalBytes = RuleInvocationCodec.TerminalBytes(result).Length;
        var deliveryBytes = invocation.NativeDestination is { } destination
            ? RuleInvocationCodec.DeliveryRecordBytes(longestEncodedIdentifier, destination, result).Length
            : 0;
        var factsBytes = RuleJson.Write(writer => RulesCodec.WriteFacts(writer, vocabulary), RuleLimits.MaxEnvelopeBytes).Length;

        // Count pending receipt AND terminal even though a normal state has at most one. Historical
        // receipts are separate rows; their aggregate size is intentionally not included here.
        // Count both copies if a native outbox stores an embedded terminal payload separately.
        var projectionBytes = checked(common.Length + factsBytes + receiptBytes + terminalBytes + deliveryBytes + RuleLimits.MaxStateMetadataBytes);
        if (projectionBytes > RuleLimits.MaxStateProjectionBytes)
            throw new InvalidDataException("rules.state-capacity");
        return new(common.Length, receiptBytes, terminalBytes, deliveryBytes, projectionBytes);
    }
}

internal sealed record RuleCapacityFootprint(int InvocationBytes, int MaximumReceiptBytes,
    int MaximumTerminalBytes, int MaximumDeliveryBytes, int MaximumStateProjectionBytes);
