using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;

namespace Elsa.Rules.Tests;

public class RuleRoundTests
{
    private readonly RuleRoundEvaluator _evaluator = new();

    [Fact]
    public async Task Witness_uses_frozen_inputs_and_records_all_firings_through_closure()
    {
        var facts = Facts("seed");
        string[][] expectedAdditions = [["left", "right"], ["joined"], ["final"], []];
        string[][] expectedFirings = [["left", "right"], ["join", "left", "right"], ["final", "join", "left", "right"], ["final", "join", "left", "right"]];
        for (var ordinal = 0; ordinal < expectedAdditions.Length; ordinal++)
        {
            var input = Input(Witness(), facts, ordinal);
            var receipt = _evaluator.Evaluate(input);
            Assert.Equal(expectedAdditions[ordinal], receipt.AddedFacts.Select(fact => fact.Atom));
            Assert.Equal(expectedFirings[ordinal], receipt.FiringEvidence.Select(firing => firing.RuleId));
            Assert.Equal(RuleValidationDisposition.Equivalent, (await _evaluator.ValidateAsync(new(input), receipt)).Disposition);
            facts = RulesCodec.CanonicalFacts(facts.Concat(receipt.AddedFacts));
        }
        Assert.Equal(["final", "joined", "left", "right", "seed"], facts.Select(fact => fact.Atom));
    }

    [Fact]
    public async Task Already_present_conclusions_still_require_their_firing_evidence()
    {
        var input = Input(Witness(), Facts("left", "right", "seed"), 1);
        var complete = _evaluator.Evaluate(input);
        var incomplete = Receipt(complete, firings: complete.FiringEvidence.Where(firing => firing.RuleId == "join").ToArray());
        Assert.Equal(["joined"], incomplete.AddedFacts.Select(fact => fact.Atom));
        Assert.Equal(RuleValidationDisposition.Invalid, (await _evaluator.ValidateAsync(new(input), incomplete)).Disposition);
    }

    [Fact]
    public async Task Rehashed_omitted_additions_are_rejected()
    {
        var input = Input(Witness(), Facts("seed"));
        var incomplete = Receipt(_evaluator.Evaluate(input), additions: Facts("left"));
        Assert.Equal(RuleValidationDisposition.Invalid, (await _evaluator.ValidateAsync(new(input), incomplete)).Disposition);
    }

    [Fact]
    public async Task Reordered_duplicate_or_extraneous_receipt_data_is_rejected()
    {
        var input = Input(Witness(), Facts("seed"));
        var complete = _evaluator.Evaluate(input);
        var candidates = new[]
        {
            Receipt(complete, additions: Facts("right", "left")),
            Receipt(complete, additions: Facts("left", "left", "right")),
            Receipt(complete, additions: Facts("joined", "left", "right")),
            Receipt(complete, firings: complete.FiringEvidence.Reverse().ToArray()),
            Receipt(complete, firings: complete.FiringEvidence.Concat(complete.FiringEvidence).ToArray())
        };
        foreach (var candidate in candidates)
            Assert.Equal(RuleValidationDisposition.Invalid, (await _evaluator.ValidateAsync(new(input), candidate)).Disposition);
    }

    [Fact]
    public async Task Changed_logical_identity_is_not_an_equivalent_receipt()
    {
        var input = Input(Witness(), Facts("seed"));
        var original = _evaluator.Evaluate(input);
        var changedIdentity = new RuleRoundIdentity(new RuleRunIdentity(new RuleScope("other", "partition"), "invocation"),
            original.Identity.Fingerprint, original.Identity.Ordinal, original.Identity.InputStateSha256);
        var changed = Receipt(original, identity: changedIdentity);
        Assert.Equal(RuleValidationDisposition.Invalid, (await _evaluator.ValidateAsync(new(input), changed)).Disposition);
    }

    [Fact]
    public void Empty_program_closes_and_empty_conjunction_fires()
    {
        var empty = Program([], []);
        Assert.Equal(1, RulesCodec.ResolveMaxRounds(empty, [], null));
        Assert.Empty(_evaluator.Evaluate(Input(empty, [])).AddedFacts);
        var unconditional = Program(["constant"], [Rule("always", [], "constant")]);
        Assert.Equal(["constant"], _evaluator.Evaluate(Input(unconditional, [])).AddedFacts.Select(fact => fact.Atom));
    }

    [Fact]
    public void Canonical_artifact_is_independent_of_authoring_order()
    {
        var program = Witness();
        var reordered = new RuleProgram(program.FormatVersion, program.LanguageId, program.LanguageVersion,
            program.Vocabulary.Reverse().ToArray(), program.Rules.Reverse().ToArray());
        var original = RulesCodec.CreateArtifact(program);
        Assert.Equal(original.Sha256, RulesCodec.CreateArtifact(reordered).Sha256);
        Assert.Equal(original.Sha256, RulesCodec.CreateArtifact(RulesCodec.ReadArtifact(original)).Sha256);
    }

    [Fact]
    public void Noncanonical_artifact_bytes_are_rejected_even_with_matching_content_hash()
    {
        var artifact = RulesCodec.CreateArtifact(Witness());
        var padded = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(artifact.CanonicalBytes.Span));
        var hash = RulesCodec.Hash(padded);
        Assert.Throws<InvalidDataException>(() => RulesCodec.ReadArtifact(new(1, "rules:" + hash, hash, padded)));
    }

    [Fact]
    public void Vocabulary_is_closed_for_premises_conclusions_and_initial_facts()
    {
        Assert.Throws<InvalidDataException>(() => RulesCodec.CreateArtifact(Program(["known"], [Rule("r", ["missing"], "known")])));
        Assert.Throws<InvalidDataException>(() => RulesCodec.CreateArtifact(Program(["known"], [Rule("r", ["known"], "missing")])));
        Assert.Throws<InvalidDataException>(() => RulesCodec.ResolveMaxRounds(Witness(), Facts("missing"), null));
    }

    [Fact]
    public void Duplicate_vocabulary_rule_ids_and_premises_are_invalid()
    {
        Assert.Throws<InvalidDataException>(() => RulesCodec.CreateArtifact(Program(["a", "a"], [])));
        Assert.Throws<InvalidDataException>(() => RulesCodec.CreateArtifact(Program(["a"], [Rule("r", [], "a"), Rule("r", ["a"], "a")])));
        Assert.Throws<InvalidDataException>(() => RulesCodec.CreateArtifact(Program(["a"], [Rule("r", ["a", "a"], "a")])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4098)]
    public void Invalid_explicit_budget_is_rejected(int budget) =>
        Assert.Throws<InvalidDataException>(() => RulesCodec.ResolveMaxRounds(Witness(), Facts("seed"), budget));

    [Fact]
    public void Default_budget_counts_absent_vocabulary_plus_closure_and_preserves_explicit_lower_limit()
    {
        Assert.Equal(7, RulesCodec.ResolveMaxRounds(Witness(), Facts("seed"), null));
        Assert.Equal(3, RulesCodec.ResolveMaxRounds(Witness(), Facts("seed"), 3));
        Assert.Equal(4, RulesCodec.ResolveMaxRounds(Witness(), Facts("seed"), 4));
    }

    [Theory]
    [InlineData('x', 256)]
    [InlineData('é', 128)]
    public void Identifier_limit_measures_utf8_bytes(char character, int length)
    {
        var exact = new string(character, length);
        RulesCodec.CreateArtifact(Program([exact], []));
        Assert.ThrowsAny<ArgumentException>(() => RulesCodec.CreateArtifact(Program([exact + "x"], [])));
    }

    [Fact]
    public void Invalid_unicode_is_not_replaced_before_hashing() =>
        Assert.ThrowsAny<ArgumentException>(() => RulesCodec.CreateArtifact(Program(["\ud800"], [])));

    [Fact]
    public void Round_input_must_be_canonical_and_match_its_digest()
    {
        Assert.Throws<InvalidDataException>(() => _evaluator.Evaluate(Input(Witness(), Facts("seed", "left"))));
        var input = Input(Witness(), Facts("seed"));
        var badIdentity = new RuleRoundIdentity(input.Identity.Run, input.Identity.Fingerprint, 0, new string('0', 64));
        Assert.Throws<InvalidDataException>(() => _evaluator.Evaluate(new(badIdentity, input.Artifact,
            input.Implementation, input.Facts, input.MaxRounds, input.ModelRevision)));
    }

    [Fact]
    public async Task Unsupported_input_schema_is_distinct_from_invalid_receipt()
    {
        var input = Input(Witness(), Facts("seed"));
        var receipt = _evaluator.Evaluate(input);
        var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(input.Artifact.CanonicalBytes.Span).Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal));
        var hash = RulesCodec.Hash(bytes);
        var unsupported = new RuleArtifactSnapshot(2, "rules:" + hash, hash, bytes);
        var next = new RuleRoundInput(input.Identity, unsupported, input.Implementation, input.Facts, input.MaxRounds, input.ModelRevision);
        Assert.Equal(RuleValidationDisposition.UnsupportedFormat, (await _evaluator.ValidateAsync(new(next), receipt)).Disposition);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"formatVersion\":\"wrong\"}")]
    [InlineData("{\"formatVersion\":1,\"languageId\":true}")]
    public async Task Malformed_json_shapes_return_invalid(string json)
    {
        var input = Input(Witness(), Facts("seed"));
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = RulesCodec.Hash(bytes);
        var malformed = new RuleArtifactSnapshot(1, "rules:" + hash, hash, bytes);
        var next = new RuleRoundInput(input.Identity, malformed, input.Implementation, input.Facts, input.MaxRounds, input.ModelRevision);
        Assert.Equal(RuleValidationDisposition.Invalid,
            (await _evaluator.ValidateAsync(new(next), _evaluator.Evaluate(input))).Disposition);
    }

    [Fact]
    public async Task Invalid_artifact_identity_is_rejected_before_unsupported_version()
    {
        var input = Input(Witness(), Facts("seed"));
        var mismatched = new RuleArtifactSnapshot(2, "rules:wrong-id", input.Artifact.Sha256, input.Artifact.CanonicalBytes);
        var next = new RuleRoundInput(input.Identity, mismatched, input.Implementation, input.Facts, input.MaxRounds, input.ModelRevision);
        Assert.Equal(RuleValidationDisposition.Invalid,
            (await _evaluator.ValidateAsync(new(next), _evaluator.Evaluate(input))).Disposition);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_to_model_failure()
    {
        var input = Input(Witness(), Facts("seed"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _evaluator.ValidateAsync(new(input),
            _evaluator.Evaluate(input), new CancellationToken(true)).AsTask());
    }

    private static RuleRoundReceipt Receipt(RuleRoundReceipt source, IReadOnlyList<RuleFact>? additions = null,
        IReadOnlyList<RuleFiringEvidence>? firings = null, RuleRoundIdentity? identity = null)
    {
        var unsigned = new RuleRoundReceipt(identity ?? source.Identity, source.ImplementationPinHash,
            additions ?? source.AddedFacts, firings ?? source.FiringEvidence, new string('0', 64));
        return new(unsigned.Identity, unsigned.ImplementationPinHash, unsigned.AddedFacts, unsigned.FiringEvidence,
            RulesCodec.Hash(RulesCodec.ReceiptBytes(unsigned)));
    }

    private static RuleRoundInput Input(RuleProgram program, IReadOnlyList<RuleFact> facts, int ordinal = 0)
    {
        var identity = new RuleRoundIdentity(new(new(null, "partition"), "invocation"), new(new string('a', 64)),
            ordinal, RulesCodec.FactsHash(facts));
        // Synthetic identity is only a semantic unit-test fixture, never package qualification.
        var asset = new RuleAssetPin("lib/net10.0/Tests.Semantics.dll", "Tests.Semantics, Version=1.0.0.0", new string('0', 64));
        var pin = new RuleSemanticPin(1, [new RulePackagePin("Tests.Semantics", "1.0.0", Convert.ToBase64String(new byte[64]), [asset])], RulesCodec.LanguageId, 1, 1, 1);
        return new(identity, RulesCodec.CreateArtifact(program), pin, facts, 8, ordinal + 1);
    }

    private static RuleProgram Witness() => Program(["final", "joined", "left", "never-derived", "right", "seed", "unreachable"],
        [Rule("left", ["seed"], "left"), Rule("right", ["seed"], "right"), Rule("join", ["left", "right"], "joined"),
         Rule("final", ["joined"], "final"), Rule("unreachable", ["unreachable"], "never-derived")]);

    private static RuleDefinition Rule(string id, string[] premises, string conclusion) => new(id, Facts(premises), new(conclusion));
    private static RuleFact[] Facts(params string[] facts) => facts.Select(fact => new RuleFact(fact)).ToArray();
    private static RuleProgram Program(string[] vocabulary, RuleDefinition[] rules) => new(1, RulesCodec.LanguageId, 1, Facts(vocabulary), rules);
}
