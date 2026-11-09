using Elsa.Rules.Persistence.Core;
using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Runtime;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public class RuleInvocationTests
{
    [Fact]
    public void Scoped_identity_is_length_framed_and_preserves_exact_unicode_and_null()
    {
        RuleRunIdentity[] identities =
        [
            new(new(null, "ab"), "c"), new(new(null, "a"), "bc"), new(new("null", "ab"), "c"),
            new(new("Tenant", "ab"), "c"), new(new("tenant", "ab"), "c"),
            new(new("é", "ab"), "c"), new(new("e\u0301", "ab"), "c"), new(new(null, "ab "), "c")
        ];
        var encodings = identities.Select(RuleRecordCodec.ScopeBytes).ToArray();
        Assert.Equal(identities.Length, encodings.Select(bytes => RuleRecordCodec.Hash(bytes)).Distinct().Count());
        Assert.Equal("01000000FFFFFFFF0200000061620100000063", Convert.ToHexString(encodings[0]));
    }

    [Fact]
    public void Fingerprint_binds_frozen_budget_artifact_pin_scope_and_destination()
    {
        var original = Invocation();
        var otherArtifact = RulesCodec.CreateArtifact(Program(["seed"], []));
        var pin = original.ExpectedImplementation;
        var changedPin = new RuleSemanticPin(pin.PinSchemaVersion, pin.Packages, pin.LanguageId, 2, 1, 1);
        var baseline = RuleRecordCodec.Fingerprint(original);
        RuleInvocationEnvelope[] changed =
        [
            Copy(original, maxRounds: original.MaxRounds - 1), Copy(original, artifact: otherArtifact),
            Copy(original, pin: changedPin), Copy(original, identity: new(new("other", "partition"), "invocation")),
            Copy(original, facts: Facts("left", "seed")), Copy(original, destination: Destination())
        ];
        Assert.All(changed, invocation => Assert.NotEqual(baseline, RuleRecordCodec.Fingerprint(invocation)));
        var native = Copy(original, destination: Destination());
        Assert.NotEqual(RuleRecordCodec.Fingerprint(native),
            RuleRecordCodec.Fingerprint(Copy(original, destination: Destination("another-bookmark"))));
    }

    [Fact]
    public void Common_identity_accepts_opaque_old_semantics_but_not_corrupt_artifact_bytes()
    {
        var original = Invocation();
        var bytes = Encoding.UTF8.GetBytes("opaque old artifact bytes that this interpreter cannot read");
        var hash = RuleRecordCodec.Hash(bytes);
        var opaque = new RuleArtifactSnapshot(99, "rules:" + hash, hash, bytes);
        var old = Copy(original, artifact: opaque,
            pin: new(99, original.ExpectedImplementation.Packages, "old.language", 99, 99, 99));
        Assert.NotEmpty(RuleRecordCodec.InvocationBytes(old));
        Assert.Throws<NotSupportedException>(() => RuleAdmissionCapacity.Measure(old));
        var corrupt = new RuleArtifactSnapshot(99, opaque.ArtifactId, hash, Encoding.UTF8.GetBytes("changed"));
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.Fingerprint(Copy(old, artifact: corrupt)));
    }

    [Fact]
    public void Canonical_identity_ignores_fact_and_package_order_but_rejects_duplicates()
    {
        var first = Invocation(Facts("left", "seed"));
        var pin = first.ExpectedImplementation;
        var extra = new RulePackagePin("Tests.Second", "1.0.0", pin.Packages[0].NuGetContentHash, pin.Packages[0].SelectedAssets);
        var two = new RuleSemanticPin(1, [pin.Packages[0], extra], pin.LanguageId, 1, 1, 1);
        var reverse = new RuleSemanticPin(1, [extra, pin.Packages[0]], pin.LanguageId, 1, 1, 1);
        Assert.Equal(RuleRecordCodec.Fingerprint(Copy(first, pin: two)),
            RuleRecordCodec.Fingerprint(Copy(first, pin: reverse, facts: first.InitialFacts.Reverse().ToArray())));
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.Fingerprint(Copy(first, facts: Facts("seed", "seed"))));
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.Fingerprint(Copy(first,
            pin: new(1, [extra, extra], pin.LanguageId, 1, 1, 1))));
    }

    [Fact]
    public void Unsupported_common_schema_and_ambiguous_package_hash_are_not_accepted()
    {
        var original = Invocation();
        var unknown = new RuleInvocationEnvelope(original.Identity, original.Artifact, original.InitialFacts,
            original.MaxRounds, original.ExpectedImplementation, 2, null);
        Assert.Throws<NotSupportedException>(() => RuleRecordCodec.InvocationBytes(unknown));
        var package = original.ExpectedImplementation.Packages[0];
        var bad = new RulePackagePin(package.PackageId, package.NuGetVersion, "not-base64", package.SelectedAssets);
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.InvocationBytes(Copy(original,
            pin: new(1, [bad], RuleLanguage.Id, 1, 1, 1))));
    }

    [Fact]
    public void Admission_footprint_covers_every_witness_receipt_and_native_terminal()
    {
        var invocation = Copy(Invocation(), destination: Destination());
        var footprint = RuleAdmissionCapacity.Measure(invocation);
        var facts = invocation.InitialFacts.ToArray();
        for (var round = 0; round < 4; round++)
        {
            var receipt = new RuleRoundEvaluator().Evaluate(Input(Witness(), facts, round, invocation.MaxRounds));
            Assert.True(RuleRecordCodec.ReceiptRecordBytes(receipt).Length <= footprint.MaximumReceiptBytes);
            facts = RuleRecordCodec.CanonicalFacts(facts.Concat(receipt.AddedFacts));
        }
        var result = new RuleTerminalResult("terminal", RuleTerminalKind.Succeeded, 4, 1, facts, RuleRecordCodec.FactsHash(facts), null);
        Assert.True(RuleRecordCodec.DeliveryRecordBytes("delivery", invocation.NativeDestination!, result).Length <= footprint.MaximumDeliveryBytes);
        Assert.InRange(footprint.MaximumStateProjectionBytes, 1, RuleLimits.MaxStateProjectionBytes);
        var direct = RuleAdmissionCapacity.Measure(Invocation());
        Assert.Equal(0, direct.MaximumDeliveryBytes);
        Assert.True(footprint.MaximumStateProjectionBytes >= direct.MaximumStateProjectionBytes + footprint.MaximumDeliveryBytes);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Unsupported_result_schema_remains_inspectable_but_cannot_pass_new_admission_capacity(int version)
    {
        var invocation = Copy(Invocation(), destination: Destination(resultVersion: version));
        Assert.NotEmpty(RuleRecordCodec.InvocationBytes(invocation));
        Assert.Throws<NotSupportedException>(() => RuleAdmissionCapacity.Measure(invocation));
    }

    [Theory]
    [InlineData(RuleLimits.MaxEnvelopeBytes)]
    [InlineData(RuleLimits.MaxStateProjectionBytes)]
    public void Canonical_writer_accepts_exact_representation_cap_and_rejects_one_over(int cap)
    {
        // The two JSON quotation marks are part of the cap.
        var exact = new string('x', cap - 2);
        Assert.Equal(cap, RuleJson.Write(writer => writer.WriteStringValue(exact), cap).Length);
        Assert.Throws<InvalidDataException>(() => RuleJson.Write(writer => writer.WriteStringValue(exact + "x"), cap));
    }

    [Fact]
    public void Terminal_digest_must_match_complete_canonical_facts()
    {
        var facts = Facts("left", "seed");
        var result = new RuleTerminalResult("terminal", RuleTerminalKind.Cancelled, 1, 1, facts, new string('0', 64), null);
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.TerminalBytes(result));
    }

    [Fact]
    public void Common_invocation_accepts_exact_four_mib_and_rejects_one_encoded_byte_over()
    {
        var source = Invocation();
        var one = WithAssets(1);
        var baseLength = RuleRecordCodec.InvocationBytes(one).Length;
        var entryBytes = RuleRecordCodec.InvocationBytes(WithAssets(2)).Length - baseLength;
        var count = 1 + (RuleLimits.MaxEnvelopeBytes - baseLength) / entryBytes;
        var candidate = WithAssets(count);
        var remaining = RuleLimits.MaxEnvelopeBytes - RuleRecordCodec.InvocationBytes(candidate).Length;
        var assets = candidate.ExpectedImplementation.Packages[0].SelectedAssets.ToArray();
        for (var index = 0; remaining > 0; index++)
        {
            var padding = Math.Min(remaining, RuleLimits.MaxIdentifierUtf8Bytes - 1);
            var asset = assets[index];
            assets[index] = new(asset.PackageRelativeAssetPath, "A" + new string('x', padding), asset.AssetSha256);
            remaining -= padding;
        }
        var exact = WithAssets(count, assets);
        Assert.Equal(RuleLimits.MaxEnvelopeBytes, RuleRecordCodec.InvocationBytes(exact).Length);
        Assert.Equal(RuleLimits.MaxEnvelopeBytes, RuleAdmissionCapacity.Measure(exact).InvocationBytes);
        var last = assets[^1];
        assets[^1] = new(last.PackageRelativeAssetPath, last.AssemblyFullName + "x", last.AssetSha256);
        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.InvocationBytes(WithAssets(count, assets)));

        RuleInvocationEnvelope WithAssets(int assetCount, IReadOnlyList<RuleAssetPin>? selected = null)
        {
            selected ??= Enumerable.Range(0, assetCount)
                .Select(index => new RuleAssetPin($"lib/net10.0/a{index:D5}.dll", "A", new string('a', 64))).ToArray();
            // Large synthetic pins qualify encoding bounds only, never installed-package identity.
            var package = new RulePackagePin("Test.Encoding", "1.0.0", Convert.ToBase64String(new byte[64]), selected);
            return Copy(source, pin: new(1, [package], RuleLanguage.Id, 1, 1, 1));
        }
    }

}
