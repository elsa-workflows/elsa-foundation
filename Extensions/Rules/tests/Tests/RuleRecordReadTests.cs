using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public class RuleRecordReadTests
{
    [Fact]
    public void Common_invocation_roundtrip_preserves_opaque_language_and_future_semantic_pin()
    {
        var original = Invocation();
        var opaque = Encoding.UTF8.GetBytes("old interpreter artifact");
        var hash = RuleRecordCodec.Hash(opaque);
        var invocation = Copy(original, artifact: new(99, "rules:" + hash, hash, opaque),
            pin: new(99, original.ExpectedImplementation.Packages, "old.language", 99, 99, 99), destination: Destination());
        var bytes = RuleRecordCodec.InvocationBytes(invocation);
        var recovered = RuleRecordCodec.ReadInvocation(bytes);
        Assert.Equal(bytes, RuleRecordCodec.InvocationBytes(recovered));
        Assert.Equal(RuleRecordCodec.Fingerprint(invocation), RuleRecordCodec.Fingerprint(recovered));
        Assert.Equal(opaque, recovered.Artifact.CanonicalBytes.ToArray());
    }

    [Fact]
    public void Complete_receipt_roundtrip_preserves_all_firings_and_checks_digest()
    {
        var receipt = new RuleRoundEvaluator().Evaluate(Input(Witness(), Facts("left", "right", "seed"), 1));
        var bytes = RuleRecordCodec.ReceiptRecordBytes(receipt);
        var recovered = RuleRecordCodec.ReadReceiptRecord(bytes);
        Assert.Equal(bytes, RuleRecordCodec.ReceiptRecordBytes(recovered));
        Assert.Equal(3, recovered.FiringEvidence.Count);
        var corrupt = new RuleRoundReceipt(receipt.Identity, receipt.ImplementationPinHash,
            receipt.AddedFacts, receipt.FiringEvidence, new string('0', 64));
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.ReadReceiptRecord(RuleRecordCodec.ReceiptRecordBytes(corrupt)));
    }

    [Fact]
    public void Terminal_and_delivery_roundtrip_reject_changed_fact_and_delivery_hashes()
    {
        var facts = Facts("left", "seed");
        var terminal = new RuleTerminalResult("terminal", RuleTerminalKind.Cancelled, 1, 1, facts,
            RuleRecordCodec.FactsHash(facts), new("rules.cancelled"));
        var bytes = RuleRecordCodec.TerminalBytes(terminal);
        var recovered = RuleRecordCodec.ReadTerminal(bytes);
        Assert.Equal(bytes, RuleRecordCodec.TerminalBytes(recovered));
        var altered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"left\"", "\"right\"", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.ReadTerminal(altered));
        var delivery = RuleRecordCodec.DeliveryRecordBytes("delivery", Destination(), terminal);
        var envelope = RuleRecordCodec.ReadDeliveryRecord(delivery);
        Assert.Equal(delivery, RuleRecordCodec.DeliveryRecordBytes(envelope.DeliveryId, envelope.Destination, envelope.Result));
        var corrupt = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(delivery)
            .Replace(envelope.CanonicalPayloadSha256, new string('0', 64), StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.ReadDeliveryRecord(corrupt));
    }

    [Fact]
    public void Unsupported_terminal_schema_is_distinct_from_malformed_records()
    {
        var result = new RuleTerminalResult("terminal", RuleTerminalKind.Cancelled, 0, 1, [],
            RuleRecordCodec.FactsHash([]), null);
        var encoded = Encoding.UTF8.GetString(RuleRecordCodec.TerminalBytes(result));
        var unknown = Encoding.UTF8.GetBytes(encoded.Replace("\"resultSchemaVersion\":1", "\"resultSchemaVersion\":2", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => RuleRecordCodec.ReadTerminal(unknown));
        var badKind = Encoding.UTF8.GetBytes(encoded.Replace("Cancelled", "InvalidKind", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => RuleRecordCodec.ReadTerminal(badKind));
    }

    [Theory]
    [InlineData("[\"seed\",\"left\"]")]
    [InlineData("[\"seed\",\"seed\"]")]
    [InlineData("[\"seed\"] ")]
    public void Fact_records_reject_noncanonical_or_duplicate_data(string value) =>
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.ReadFacts(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public void Bounded_common_records_reject_unknown_fields_and_oversized_input()
    {
        var bytes = RuleRecordCodec.InvocationBytes(Invocation());
        var extended = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)[..^1] + ",\"unexpected\":true}");
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.ReadInvocation(extended));
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.ReadInvocation(new byte[RuleLimits.MaxEnvelopeBytes + 1]));
        var facts = Facts("left", "seed");
        Assert.Equal(facts, RuleRecordCodec.ReadFacts(RuleRecordCodec.FactsBytes(facts)));
    }
}
