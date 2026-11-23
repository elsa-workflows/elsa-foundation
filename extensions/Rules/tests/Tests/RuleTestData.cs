using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Runtime;

namespace Elsa.Rules.Tests;

internal static class RuleTestData
{
    internal static RuleRoundInput Input(RuleProgram program, IReadOnlyList<RuleFact> facts, int ordinal = 0, int maxRounds = 8)
    {
        var identity = new RuleRoundIdentity(new(new(null, "partition"), "invocation"), new(new string('a', 64)),
            ordinal, RuleRecordCodec.FactsHash(facts));
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

    internal static RuleInvocationEnvelope Invocation(IReadOnlyList<RuleFact>? facts = null)
    {
        var initial = facts ?? Facts("seed");
        var input = Input(Witness(), initial);
        return new(input.Identity.Run, input.Artifact, initial, RulesCodec.ResolveMaxRounds(Witness(), initial, null), input.Implementation, 1, null);
    }

    internal static RuleInvocationEnvelope Copy(RuleInvocationEnvelope source, RuleRunIdentity? identity = null,
        RuleArtifactSnapshot? artifact = null, IReadOnlyList<RuleFact>? facts = null, int? maxRounds = null,
        RuleSemanticPin? pin = null, RuleNativeResultAddress? destination = null) =>
        new(identity ?? source.Identity, artifact ?? source.Artifact, facts ?? source.InitialFacts,
            maxRounds ?? source.MaxRounds, pin ?? source.ExpectedImplementation, source.InvocationSchemaVersion,
            destination ?? source.NativeDestination);

    internal static RuleNativeResultAddress Destination(string bookmark = "bookmark", int resultVersion = 1, int formatVersion = 1) => new(formatVersion, "workflow", "activity",
        "executable", new string('a', 64), "node", bookmark, "resume", "registration", "stimulus", new string('b', 64),
        "result", resultVersion, "deduplicate", "provider");
}
