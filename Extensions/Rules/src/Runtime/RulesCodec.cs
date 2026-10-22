using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

/// <summary>Version-one canonical encoding. Field order and ordinal set order are part of the persisted contract.</summary>
internal static class RulesCodec
{
    internal const string LanguageId = RuleLanguage.Id;

    public static RuleArtifactSnapshot CreateArtifact(RuleProgram program)
    {
        ValidateProgram(program);
        var bytes = RuleJson.Write(writer => WriteProgram(writer, program), RuleLimits.MaxArtifactBytes);
        Require(bytes.Length <= RuleLimits.MaxArtifactBytes, "rules.artifact-too-large");
        var hash = RuleRecordCodec.Hash(bytes);
        return new RuleArtifactSnapshot(1, "rules:" + hash, hash, bytes);
    }

    public static RuleProgram ReadArtifact(RuleArtifactSnapshot artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var bytes = artifact.CanonicalBytes;
        Require(bytes.Length <= RuleLimits.MaxArtifactBytes, "rules.artifact-too-large");
        Require(RuleRecordCodec.Hash(bytes.Span) == artifact.Sha256 && artifact.ArtifactId == "rules:" + artifact.Sha256,
            "rules.artifact-identity");
        if (artifact.FormatVersion != 1) throw new NotSupportedException("rules.artifact-version");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        var root = json.RootElement;
        var formatVersion = root.GetProperty("formatVersion").GetInt32();
        Require(formatVersion == artifact.FormatVersion, "rules.artifact-format-identity");
        var program = new RuleProgram(formatVersion,
            root.GetProperty("languageId").GetString()!, root.GetProperty("languageVersion").GetInt32(),
            ReadFacts(root.GetProperty("vocabulary")),
            root.GetProperty("rules").EnumerateArray().Select(rule => new RuleDefinition(
                rule.GetProperty("id").GetString()!, ReadFacts(rule.GetProperty("premises")),
                new RuleFact(rule.GetProperty("conclusion").GetString()!))).ToArray());
        var canonical = CreateArtifact(program);
        Require(canonical.CanonicalBytes.Span.SequenceEqual(bytes.Span), "rules.noncanonical-artifact");
        return program;
    }

    public static int ResolveMaxRounds(RuleProgram program, IReadOnlyList<RuleFact> initialFacts, int? requested)
    {
        ValidateProgram(program);
        var facts = RuleRecordCodec.CanonicalFacts(initialFacts);
        var vocabulary = program.Vocabulary.Select(fact => fact.Atom).ToHashSet(StringComparer.Ordinal);
        Require(facts.All(fact => vocabulary.Contains(fact.Atom)), "rules.fact-outside-vocabulary");
        var limit = requested ?? vocabulary.Count - facts.Length + 1;
        Require(limit is > 0 and <= RuleLimits.MaxRounds, "rules.invalid-round-limit");
        return limit;
    }

    private static void ValidateProgram(RuleProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (program.FormatVersion != 1 || program.LanguageId != LanguageId || program.LanguageVersion != 1)
            throw new NotSupportedException("rules.language-version");
        var vocabulary = RuleRecordCodec.CanonicalFacts(program.Vocabulary).Select(fact => fact.Atom).ToHashSet(StringComparer.Ordinal);
        Require(program.Rules.Count <= RuleLimits.MaxRules, "rules.too-many-rules");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in program.Rules)
        {
            Require(rule is not null, "rules.null-rule");
            RuleValueValidation.Identifier(rule!.Id, nameof(program));
            Require(ids.Add(rule.Id), "rules.duplicate-rule");
            Require(rule.Premises.Count <= RuleLimits.MaxPremisesPerRule, "rules.too-many-premises");
            var premises = RuleRecordCodec.CanonicalFacts(rule.Premises);
            RuleValueValidation.Identifier(rule.Conclusion?.Atom!, nameof(program));
            Require(premises.All(fact => vocabulary.Contains(fact.Atom)) && vocabulary.Contains(rule.Conclusion!.Atom),
                "rules.rule-outside-vocabulary");
        }
    }

    private static RuleFact[] ReadFacts(JsonElement array) => array.EnumerateArray()
        .Take(RuleLimits.MaxVocabularyFacts + 1).Select(value => new RuleFact(value.GetString()!)).ToArray();

    private static void WriteProgram(Utf8JsonWriter writer, RuleProgram program)
    {
        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", program.FormatVersion);
        writer.WriteString("languageId", program.LanguageId);
        writer.WriteNumber("languageVersion", program.LanguageVersion);
        writer.WritePropertyName("vocabulary");
        writer.WriteRawValue(RuleRecordCodec.FactsBytes(program.Vocabulary));
        writer.WriteStartArray("rules");
        foreach (var rule in program.Rules.OrderBy(rule => rule.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", rule.Id);
            writer.WritePropertyName("premises");
            writer.WriteRawValue(RuleRecordCodec.FactsBytes(rule.Premises));
            writer.WriteString("conclusion", rule.Conclusion.Atom);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }
}
