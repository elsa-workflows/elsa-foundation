using System.Runtime.InteropServices;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;

namespace Elsa.Rules.Tests;

public class RuleValueTests
{
    [Fact]
    public void Scope_preserves_null_case_and_unicode_distinctions()
    {
        Assert.NotEqual(new RuleScope(null, "partition"), new RuleScope("tenant", "partition"));
        Assert.NotEqual(new RuleScope("Tenant", "partition"), new RuleScope("tenant", "partition"));
        Assert.NotEqual(new RuleScope("é", "partition"), new RuleScope("e\u0301", "partition"));
        Assert.Equal(new RuleScope(null, "partition"), new RuleScope(null, "partition"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public void Scope_keys_reject_empty_and_overlong_values(int length)
    {
        var value = new string('a', length);
        Assert.ThrowsAny<ArgumentException>(() => new RuleScope(value, "partition"));
        Assert.ThrowsAny<ArgumentException>(() => new RuleScope(null, value));
        Assert.ThrowsAny<ArgumentException>(() => new RuleRunIdentity(new(null, "partition"), value));
    }

    [Fact]
    public void Collection_values_snapshot_inputs_and_preserve_order_for_validation()
    {
        RuleFact[] vocabulary = [new("z"), new("a")];
        RuleFact[] premises = [new("z")];
        var definition = new RuleDefinition("r", premises, new("a"));
        RuleDefinition[] rules = [definition];
        var program = new RuleProgram(1, RuleLanguage.Id, 1, vocabulary, rules);
        premises[0] = new("changed");
        vocabulary[0] = new("changed");
        rules[0] = new("other", [], new("changed"));
        Assert.Equal(["z", "a"], program.Vocabulary.Select(fact => fact.Atom));
        Assert.Equal("z", program.Rules[0].Premises[0].Atom);
        Assert.Throws<NotSupportedException>(() => ((IList<RuleFact>)program.Vocabulary)[0] = new("changed"));
    }

    [Fact]
    public void Artifact_bytes_are_snapshotted_on_input_and_output()
    {
        byte[] bytes = [1, 2, 3];
        var snapshot = new RuleArtifactSnapshot(1, "artifact", RulesCodec.Hash(bytes), bytes);
        bytes[0] = 9;
        var exposed = snapshot.CanonicalBytes;
        Assert.True(MemoryMarshal.TryGetArray(exposed, out var segment));
        segment.Array![segment.Offset] = 8;
        Assert.Equal(new byte[] { 1, 2, 3 }, snapshot.CanonicalBytes.ToArray());
    }

    [Fact]
    public void Receipt_snapshots_candidates_without_sorting_or_erasing_invalid_duplicates()
    {
        RuleFact[] facts = [new("z"), new("a"), new("a")];
        RuleFiringEvidence[] firings = [new("z", new("z")), new("a", new("a"))];
        var identity = new RuleRoundIdentity(new(new(null, "partition"), "invocation"), new(new string('0', 64)), 0, new string('0', 64));
        var receipt = new RuleRoundReceipt(identity, new string('0', 64), facts, firings, new string('0', 64));
        facts[0] = new("changed");
        firings[0] = new("changed", new("changed"));
        Assert.Equal(["z", "a", "a"], receipt.AddedFacts.Select(fact => fact.Atom));
        Assert.Equal(["z", "a"], receipt.FiringEvidence.Select(firing => firing.RuleId));
    }
}
