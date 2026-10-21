using Elsa.Rules.Persistence.Core;
using System.Globalization;
using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Runtime;

namespace Elsa.Rules.Tests;

public class RuleCountBoundaryTests
{
    [Fact]
    public void Rule_and_vocabulary_counts_accept_the_limit_and_reject_one_over()
    {
        var vocabulary = Enumerable.Range(0, RuleLimits.MaxVocabularyFacts)
            .Select(index => Fact($"v{index:D4}"))
            .ToArray();
        var vocabularyArtifact = RulesCodec.CreateArtifact(Program(vocabulary, []));
        Assert.Equal(RuleLimits.MaxVocabularyFacts, RulesCodec.ReadArtifact(vocabularyArtifact).Vocabulary.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Program([.. vocabulary, Fact("one-over")], []));

        var rules = Enumerable.Range(0, RuleLimits.MaxRules)
            .Select(index => new RuleDefinition($"r{index:D4}", [], vocabulary[0]))
            .ToArray();
        var ruleArtifact = RulesCodec.CreateArtifact(Program([vocabulary[0]], rules));
        Assert.Equal(RuleLimits.MaxRules, RulesCodec.ReadArtifact(ruleArtifact).Rules.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Program([vocabulary[0]], [.. rules, new("one-over", [], vocabulary[0])]));
    }

    [Fact]
    public void Premise_count_accepts_64_and_rejects_65()
    {
        var vocabulary = Enumerable.Range(0, RuleLimits.MaxPremisesPerRule)
            .Select(index => Fact($"p{index:D2}"))
            .ToArray();
        var rule = new RuleDefinition("all", vocabulary, vocabulary[0]);
        var artifact = RulesCodec.CreateArtifact(Program(vocabulary, [rule]));

        Assert.Equal(RuleLimits.MaxPremisesPerRule, RulesCodec.ReadArtifact(artifact).Rules[0].Premises.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleDefinition("all", [.. vocabulary, Fact("one-over")], vocabulary[0]));
    }

    [Fact]
    public void Rule_identifier_limit_counts_utf8_bytes()
    {
        var conclusion = Fact("conclusion");
        Assert.Equal(256, Encoding.UTF8.GetByteCount(new string('r', 256)));
        _ = new RuleDefinition(new string('r', 256), [], conclusion);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleDefinition(new string('r', 257), [], conclusion));

        Assert.Equal(256, Encoding.UTF8.GetByteCount(new string('é', 128)));
        _ = new RuleDefinition(new string('é', 128), [], conclusion);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleDefinition(new string('é', 129), [], conclusion));
    }

    [Fact]
    public void Maximum_round_budget_is_accepted_and_one_over_is_rejected()
    {
        var empty = Program([], []);

        Assert.Equal(RuleLimits.MaxRounds, RulesCodec.ResolveMaxRounds(empty, [], RuleLimits.MaxRounds));
        Assert.Throws<InvalidDataException>(() => RulesCodec.ResolveMaxRounds(empty, [], RuleLimits.MaxRounds + 1));
    }

    [Fact]
    public void Canonical_artifact_accepts_exact_byte_limit_and_rejects_one_over_with_json_escaping()
    {
        var exactProgram = ProgramAtArtifactSize(RuleLimits.MaxArtifactBytes);
        var exact = RulesCodec.CreateArtifact(exactProgram);
        var json = Encoding.UTF8.GetString(exact.CanonicalBytes.Span);
        Assert.Equal(RuleLimits.MaxArtifactBytes, exact.CanonicalBytes.Length);
        Assert.Contains("\\u0022", json); // The canonical default encoder escapes the embedded quote.
        Assert.Contains("\\\\", json); // Embedded backslash is JSON escaped.
        Assert.Contains("\\u0001", json); // Control characters are escaped as well.

        var overVocabulary = exactProgram.Vocabulary.ToArray();
        overVocabulary[0] = Fact(overVocabulary[0].Atom + "x");
        Assert.All(overVocabulary, fact => Assert.InRange(Encoding.UTF8.GetByteCount(fact.Atom), 1, RuleLimits.MaxIdentifierUtf8Bytes));
        var oneOver = Program(overVocabulary, exactProgram.Rules);
        Assert.Throws<InvalidDataException>(() => RulesCodec.CreateArtifact(oneOver));
    }

    [Fact]
    public void Artifact_snapshot_enforces_byte_limit_independently_of_json_validity()
    {
        var exactBytes = new byte[RuleLimits.MaxArtifactBytes];
        var exact = new RuleArtifactSnapshot(1, "raw-bytes", RuleRecordCodec.Hash(exactBytes), exactBytes);
        Assert.Equal(RuleLimits.MaxArtifactBytes, exact.CanonicalBytes.Length);

        var overBytes = new byte[RuleLimits.MaxArtifactBytes + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleArtifactSnapshot(
            1, "raw-bytes-over", RuleRecordCodec.Hash(overBytes), overBytes));
    }

    [Fact]
    public void Canonical_fact_count_guard_stops_a_nonterminating_enumerable_at_one_over()
    {
        var emitted = 0;

        Assert.Throws<InvalidDataException>(() => RuleRecordCodec.CanonicalFacts(EndlessFacts()));
        Assert.Equal(RuleLimits.MaxVocabularyFacts + 1, emitted);

        IEnumerable<RuleFact> EndlessFacts()
        {
            while (true)
            {
                var index = emitted++;
                yield return Fact($"f{index.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }

    private static RuleProgram ProgramAtArtifactSize(int targetBytes)
    {
        var baseFacts = Enumerable.Range(0, RuleLimits.MaxVocabularyFacts)
            .Select(index => Fact($"fact-{index.ToString("D4", CultureInfo.InvariantCulture)}\"\\\u0001"))
            .ToArray();
        var baseProgram = Program(baseFacts, []);
        var baseLength = RulesCodec.CreateArtifact(baseProgram).CanonicalBytes.Length;
        var additionalBytes = targetBytes - baseLength;
        Assert.True(additionalBytes >= 0);

        var commonPadding = additionalBytes / baseFacts.Length;
        var remainder = additionalBytes % baseFacts.Length;
        var paddedFacts = baseFacts.Select((fact, index) => Fact(fact.Atom + new string('x', commonPadding + (index < remainder ? 1 : 0))))
            .ToArray();
        Assert.All(paddedFacts, fact => Assert.InRange(Encoding.UTF8.GetByteCount(fact.Atom), 1, RuleLimits.MaxIdentifierUtf8Bytes));
        return Program(paddedFacts, []);
    }

    private static RuleFact Fact(string atom) => new(atom);

    private static RuleProgram Program(IReadOnlyList<RuleFact> vocabulary, IReadOnlyList<RuleDefinition> rules) =>
        new(RuleLanguage.FormatVersion, RuleLanguage.Id, RuleLanguage.Version, vocabulary, rules);
}
