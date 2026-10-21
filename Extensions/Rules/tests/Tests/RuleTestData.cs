using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;

namespace Elsa.Rules.Tests;

internal static class RuleTestData
{
    internal static RuleRoundInput Input(RuleProgram program, IReadOnlyList<RuleFact> facts, int ordinal = 0, int maxRounds = 8)
    {
        var identity = new RuleRoundIdentity(new(new(null, "partition"), "invocation"), new(new string('a', 64)),
            ordinal, RulesCodec.FactsHash(facts));
        // Synthetic identity is only a semantic unit-test fixture, never package qualification.
        var asset = new RuleAssetPin("lib/net10.0/Tests.Semantics.dll", "Tests.Semantics, Version=1.0.0.0", new string('0', 64));
        var pin = new RuleSemanticPin(1, [new RulePackagePin("Tests.Semantics", "1.0.0", Convert.ToBase64String(new byte[64]), [asset])], RulesCodec.LanguageId, 1, 1, 1);
        return new(identity, RulesCodec.CreateArtifact(program), pin, facts, maxRounds, ordinal + 1);
    }

    internal static RuleProgram Witness() => Program(["final", "joined", "left", "never-derived", "right", "seed", "unreachable"],
        [Rule("left", ["seed"], "left"), Rule("right", ["seed"], "right"), Rule("join", ["left", "right"], "joined"),
         Rule("final", ["joined"], "final"), Rule("unreachable", ["unreachable"], "never-derived")]);

    internal static RuleDefinition Rule(string id, string[] premises, string conclusion) => new(id, Facts(premises), new(conclusion));
    internal static RuleFact[] Facts(params string[] facts) => facts.Select(fact => new RuleFact(fact)).ToArray();
    internal static RuleProgram Program(string[] vocabulary, RuleDefinition[] rules) => new(1, RulesCodec.LanguageId, 1, Facts(vocabulary), rules);
}
